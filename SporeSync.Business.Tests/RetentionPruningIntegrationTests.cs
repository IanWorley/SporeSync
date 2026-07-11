using Npgsql;
using SporeSync.Domain.Model;
using SporeSync.Infrastructure.Repository;

namespace SporeSync.Business.Tests;

public sealed class RetentionPruningIntegrationTests : IClassFixture<RepositoryTestcontainerFixture>
{
    private readonly RepositoryTestcontainerFixture _fixture;

    public RetentionPruningIntegrationTests(RepositoryTestcontainerFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task PruneHistory_RemovesOldTerminalRuns_ButPreservesSyncedState()
    {
        var profileRepository = new SftpConnectionProfileRepository(_fixture.DataSource);
        var jobRepository = new SporeSyncJobRepository(_fixture.DataSource);
        var runRepository = new SporeSyncRunRepository(_fixture.DataSource);
        var queueRepository = new DownloadQueueItemRepository(_fixture.DataSource);

        var profile = await profileRepository.UpsertAsync(CreateProfile());
        var job = await jobRepository.UpsertAsync(CreateJob(profile.Id));

        var oldRun = await runRepository.CreateAsync(job.Id);
        var completedItem = await queueRepository.UpsertAsync(new UpsertDownloadQueueItem
        {
            JobId = job.Id,
            SyncRunId = oldRun.Id,
            RemotePath = "/incoming/keep-state.csv",
            DestinationPath = "/local/incoming/keep-state.csv",
            FileSizeBytes = 100,
            RemoteModifiedAt = DateTimeOffset.UtcNow,
            IsGroup = false,
            ChildCount = 0
        });
        await queueRepository.UpdateProgressAsync(new UpdateDownloadQueueItemProgress
        {
            Id = completedItem.Id,
            Status = "completed",
            BytesDownloaded = 100
        });
        await runRepository.UpdateStatusAsync(new UpdateSporeSyncRunStatus
        {
            Id = oldRun.Id,
            Status = "completed"
        });
        await BackdateRunAsync(oldRun.Id, DateTimeOffset.UtcNow.AddDays(-90));

        var recentRun = await runRepository.CreateAsync(job.Id);

        var result = await runRepository.PruneHistoryAsync(DateTimeOffset.UtcNow.AddDays(-30));

        Assert.Equal(1, result.PrunedRunCount);
        Assert.Null(await runRepository.GetByIdAsync(oldRun.Id));
        Assert.NotNull(await runRepository.GetByIdAsync(recentRun.Id));

        var syncedState = await queueRepository.GetSyncedStateAsync(job.Id);
        Assert.True(syncedState.ContainsKey("/incoming/keep-state.csv"));
        Assert.Equal("completed", syncedState["/incoming/keep-state.csv"].Status);

        var detached = await queueRepository.GetByIdAsync(completedItem.Id);
        Assert.NotNull(detached);
        Assert.Null(detached.SyncRunId);
    }

    [Fact]
    public async Task PruneHistory_RemovesStaleRemoteDeletedMarkers()
    {
        var profileRepository = new SftpConnectionProfileRepository(_fixture.DataSource);
        var jobRepository = new SporeSyncJobRepository(_fixture.DataSource);
        var runRepository = new SporeSyncRunRepository(_fixture.DataSource);
        var queueRepository = new DownloadQueueItemRepository(_fixture.DataSource);

        var profile = await profileRepository.UpsertAsync(CreateProfile());
        var job = await jobRepository.UpsertAsync(CreateJob(profile.Id));
        var run = await runRepository.CreateAsync(job.Id);

        var item = await queueRepository.UpsertAsync(new UpsertDownloadQueueItem
        {
            JobId = job.Id,
            SyncRunId = run.Id,
            RemotePath = "/incoming/deleted-long-ago.csv",
            DestinationPath = "/local/incoming/deleted-long-ago.csv",
            FileSizeBytes = 100,
            RemoteModifiedAt = DateTimeOffset.UtcNow,
            IsGroup = false,
            ChildCount = 0
        });
        await queueRepository.UpdateProgressAsync(new UpdateDownloadQueueItemProgress
        {
            Id = item.Id,
            Status = "completed",
            BytesDownloaded = 100
        });
        await queueRepository.MarkRemoteDeletedAsync(job.Id, run.Id, ["/incoming/deleted-long-ago.csv"]);
        await runRepository.UpdateStatusAsync(new UpdateSporeSyncRunStatus
        {
            Id = run.Id,
            Status = "completed"
        });
        await BackdateRunAsync(run.Id, DateTimeOffset.UtcNow.AddDays(-90));
        await BackdateQueueItemAsync(item.Id, DateTimeOffset.UtcNow.AddDays(-90));

        var result = await runRepository.PruneHistoryAsync(DateTimeOffset.UtcNow.AddDays(-30));

        Assert.Equal(1, result.PrunedRunCount);
        Assert.Equal(1, result.PrunedQueueItemCount);
        Assert.Null(await queueRepository.GetByIdAsync(item.Id));
    }

    [Fact]
    public async Task PruneHistory_KeepsActiveRuns()
    {
        var profileRepository = new SftpConnectionProfileRepository(_fixture.DataSource);
        var jobRepository = new SporeSyncJobRepository(_fixture.DataSource);
        var runRepository = new SporeSyncRunRepository(_fixture.DataSource);

        var profile = await profileRepository.UpsertAsync(CreateProfile());
        var job = await jobRepository.UpsertAsync(CreateJob(profile.Id));
        var activeRun = await runRepository.CreateAsync(job.Id);
        await runRepository.UpdateStatusAsync(new UpdateSporeSyncRunStatus
        {
            Id = activeRun.Id,
            Status = "downloading"
        });
        await BackdateRunAsync(activeRun.Id, DateTimeOffset.UtcNow.AddDays(-90));

        await runRepository.PruneHistoryAsync(DateTimeOffset.UtcNow.AddDays(-30));

        var kept = await runRepository.GetByIdAsync(activeRun.Id);
        Assert.NotNull(kept);
        Assert.Equal("downloading", kept.Status);
    }

    private static SftpConnectionProfile CreateProfile()
    {
        return new SftpConnectionProfile
        {
            Id = Guid.NewGuid(),
            Name = $"profile-{Guid.NewGuid():N}",
            Host = "sftp.example.com",
            Port = 22,
            Username = "sync-user",
            EncryptedPassword = "encrypted-password",
            IsDefault = false
        };
    }

    private static UpsertSporeSyncJob CreateJob(Guid profileId)
    {
        return new UpsertSporeSyncJob
        {
            Id = Guid.NewGuid(),
            ConnectionProfileId = profileId,
            Name = $"job-{Guid.NewGuid():N}",
            SourcePath = "/incoming",
            DestinationPath = "/local/incoming",
            PollingIntervalSeconds = 120,
            IsEnabled = true
        };
    }

    private async Task BackdateRunAsync(Guid runId, DateTimeOffset timestamp)
    {
        const string sql = """
            UPDATE core.sftp_sync_runs
            SET started_at = @timestamp,
                completed_at = CASE WHEN completed_at IS NOT NULL THEN @timestamp ELSE NULL END
            WHERE id = @run_id;
            """;

        await using var connection = await _fixture.DataSource.OpenConnectionAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("run_id", runId);
        command.Parameters.AddWithValue("timestamp", timestamp);
        await command.ExecuteNonQueryAsync();
    }

    private async Task BackdateQueueItemAsync(Guid itemId, DateTimeOffset timestamp)
    {
        const string sql = """
            UPDATE core.download_queue_items
            SET updated_at = @timestamp
            WHERE id = @item_id;
            """;

        await using var connection = await _fixture.DataSource.OpenConnectionAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("item_id", itemId);
        command.Parameters.AddWithValue("timestamp", timestamp);
        await command.ExecuteNonQueryAsync();
    }
}
