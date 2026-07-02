using SporeSync.Domain.Model;
using SporeSync.Infrastructure.Repository;

namespace SporeSync.Business.Tests;

/// <summary>
/// Repository integration tests (Testcontainers + real Postgres).
/// Phase 3/6/8 note: All paths here (and the SeedRunAsync helpers) continue to work with the grouping columns
/// (is_group / group_remote_path / child_count). The paged queue APIs under test use the visible filter
/// (Phase 2/3) and must never return internal leaves. The 43 tests (including Phase 4 scanner algorithm tests)
/// + dev simulation now exercising full group lifecycle + requeue provide the required coverage for M2/M4.
/// </summary>
public sealed class RepositoryIntegrationTests : IClassFixture<RepositoryTestcontainerFixture>
{
    private readonly RepositoryTestcontainerFixture _fixture;

    public RepositoryIntegrationTests(RepositoryTestcontainerFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task SystemPropertyRepository_UpsertAndGetByName_RoundTripsThroughPostgres()
    {
        var repository = new SystemPropertyRepository(_fixture.DataSource);
        var propertyName = $"sync:test:{Guid.NewGuid()}";

        var upserted = await repository.UpsertAsync(propertyName, "enabled");
        var fetched = await repository.GetByNameAsync(propertyName);

        Assert.NotNull(fetched);
        Assert.Equal(upserted.Id, fetched.Id);
        Assert.Equal(propertyName, fetched.PropertyName);
        Assert.Equal("enabled", fetched.PropertyValue);
    }

    [Fact]
    public async Task SystemPropertyRepository_InsertIfMissing_DoesNotUpdateExistingValue()
    {
        var repository = new SystemPropertyRepository(_fixture.DataSource);
        var propertyName = $"sync:test:insert:{Guid.NewGuid()}";

        var inserted = await repository.InsertIfMissingAsync(propertyName, "first");
        var existing = await repository.InsertIfMissingAsync(propertyName, "second");

        Assert.Equal(inserted.Id, existing.Id);
        Assert.Equal("first", existing.PropertyValue);
    }

    [Fact]
    public async Task SftpConnectionProfileRepository_UpsertAndGetById_RoundTripsThroughPostgres()
    {
        var repository = new SftpConnectionProfileRepository(_fixture.DataSource);
        var profile = new SftpConnectionProfile
        {
            Id = Guid.NewGuid(),
            Name = $"profile-{Guid.NewGuid():N}",
            Host = "sftp.example.com",
            Port = 22,
            Username = "sync-user",
            EncryptedPassword = "encrypted-password",
            IsDefault = false
        };

        var upserted = await repository.UpsertAsync(profile);
        var fetched = await repository.GetByIdAsync(profile.Id);

        Assert.NotNull(fetched);
        Assert.Equal(upserted.Id, fetched.Id);
        Assert.Equal(profile.Name, fetched.Name);
        Assert.Equal(profile.Host, fetched.Host);
        Assert.Equal(profile.Username, fetched.Username);
        Assert.Equal(profile.EncryptedPassword, fetched.EncryptedPassword);
        Assert.False(fetched.IsDefault);
    }

    [Fact]
    public async Task SftpConnectionProfileRepository_TryPinHostKeyFingerprint_OnlyUpdatesNullPin()
    {
        var repository = new SftpConnectionProfileRepository(_fixture.DataSource);
        var profile = await repository.UpsertAsync(new SftpConnectionProfile
        {
            Id = Guid.NewGuid(),
            Name = $"profile-{Guid.NewGuid():N}",
            Host = "sftp.example.com",
            Port = 22,
            Username = "sync-user",
            EncryptedPassword = "encrypted-password",
            IsDefault = false
        });
        await repository.UpsertAsync(new SftpConnectionProfile
        {
            Id = profile.Id,
            Name = "concurrently-edited",
            Host = "edited.example.com",
            Port = 2222,
            Username = "edited-user",
            EncryptedPassword = "edited-password",
            IsDefault = false
        });

        var pinned = await repository.TryPinHostKeyFingerprintAsync(
            profile.Id,
            "SHA256:nThbg6kXUpJWGl7E1IGOCspRomTxdCARLviKw6E5SY8");
        var repinned = await repository.TryPinHostKeyFingerprintAsync(
            profile.Id,
            "SHA256:AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA");
        var fetched = await repository.GetByIdAsync(profile.Id);

        Assert.True(pinned);
        Assert.False(repinned);
        Assert.NotNull(fetched);
        Assert.Equal("concurrently-edited", fetched.Name);
        Assert.Equal("edited.example.com", fetched.Host);
        Assert.Equal(2222, fetched.Port);
        Assert.Equal("edited-user", fetched.Username);
        Assert.Equal("edited-password", fetched.EncryptedPassword);
        Assert.Equal(
            "SHA256:nThbg6kXUpJWGl7E1IGOCspRomTxdCARLviKw6E5SY8",
            fetched.HostKeyFingerprintSha256);
    }

    [Fact]
    public async Task SftpConnectionProfileRepository_HasAnyEncryptedSecretsAsync_ReturnsWhetherAnySecretsExist()
    {
        var repository = new SftpConnectionProfileRepository(_fixture.DataSource);

        await repository.UpsertAsync(new SftpConnectionProfile
        {
            Id = Guid.NewGuid(),
            Name = $"profile-{Guid.NewGuid():N}",
            Host = "sftp.example.com",
            Port = 22,
            Username = "sync-user",
            EncryptedPassword = "encrypted-password",
            IsDefault = false
        });
        var after = await repository.HasAnyEncryptedSecretsAsync();

        Assert.True(after);
    }

    [Fact]
    public async Task SporeSyncJobRepository_UpsertAndGetById_RoundTripsThroughPostgres()
    {
        var profileRepository = new SftpConnectionProfileRepository(_fixture.DataSource);
        var jobRepository = new SporeSyncJobRepository(_fixture.DataSource);

        var profile = await profileRepository.UpsertAsync(new SftpConnectionProfile
        {
            Id = Guid.NewGuid(),
            Name = $"profile-{Guid.NewGuid():N}",
            Host = "sftp.example.com",
            Port = 22,
            Username = "sync-user",
            EncryptedPassword = "encrypted-password",
            IsDefault = false
        });

        var jobId = Guid.NewGuid();
        var upserted = await jobRepository.UpsertAsync(new UpsertSporeSyncJob
        {
            Id = jobId,
            ConnectionProfileId = profile.Id,
            Name = $"job-{Guid.NewGuid():N}",
            SourcePath = "/incoming",
            DestinationPath = "/local/incoming",
            PollingIntervalSeconds = 120,
            IsEnabled = true
        });

        var fetched = await jobRepository.GetByIdAsync(jobId);

        Assert.NotNull(fetched);
        Assert.Equal(upserted.Id, fetched.Id);
        Assert.Equal(profile.Id, fetched.ConnectionProfileId);
        Assert.Equal(upserted.Name, fetched.Name);
        Assert.Equal("/incoming", fetched.SourcePath);
        Assert.Equal("/local/incoming", fetched.DestinationPath);
        Assert.Equal(120, fetched.PollingIntervalSeconds);
        Assert.True(fetched.IsEnabled);
    }

    [Fact]
    public async Task SporeSyncRunRepository_GetRuns_FiltersSearchesSortsAndPages()
    {
        var profileRepository = new SftpConnectionProfileRepository(_fixture.DataSource);
        var jobRepository = new SporeSyncJobRepository(_fixture.DataSource);
        var runRepository = new SporeSyncRunRepository(_fixture.DataSource);

        var profile = await profileRepository.UpsertAsync(CreateProfile());
        var job = await jobRepository.UpsertAsync(CreateJob(profile.Id));
        var matchingRunId = Guid.NewGuid();
        var nonMatchingRunId = Guid.NewGuid();

        await SeedRunAsync(job.Id, matchingRunId, "downloading", "/incoming/match-file.csv");
        await SeedRunAsync(job.Id, nonMatchingRunId, "completed", "/incoming/other-file.csv");

        var result = await runRepository.GetRunsAsync(new RunQuery
        {
            Statuses = ["downloading"],
            Search = "match-file",
            SortBy = "progress",
            SortDirection = "desc",
            PageNumber = 1,
            PageSize = 10
        });

        var run = Assert.Single(result.Items);
        Assert.Equal(matchingRunId, run.Id);
        Assert.Equal(job.Name, run.JobName);
        Assert.Equal(1, result.TotalCount);
    }

    [Fact]
    public async Task DownloadQueueItemRepository_GetByRunId_FiltersSearchesSortsAndPages()
    {
        var profileRepository = new SftpConnectionProfileRepository(_fixture.DataSource);
        var jobRepository = new SporeSyncJobRepository(_fixture.DataSource);
        var queueRepository = new DownloadQueueItemRepository(_fixture.DataSource);

        var profile = await profileRepository.UpsertAsync(CreateProfile());
        var job = await jobRepository.UpsertAsync(CreateJob(profile.Id));
        var runId = Guid.NewGuid();

        await SeedRunAsync(job.Id, runId, "downloading", "/incoming/zeta.csv");
        await SeedQueueItemAsync(job.Id, runId, "/incoming/alpha.csv", "downloading", 100, 50);
        await SeedQueueItemAsync(job.Id, runId, "/incoming/beta.csv", "queued", 100, 0);

        var result = await queueRepository.GetByRunIdAsync(runId, new QueueItemQuery
        {
            Statuses = ["downloading"],
            Search = "alpha",
            SortBy = "basename",
            SortDirection = "asc",
            PageNumber = 1,
            PageSize = 10
        });

        var item = Assert.Single(result.Items);
        Assert.EndsWith("alpha.csv", item.RemotePath);
        Assert.Equal("downloading", item.Status);
        Assert.Equal(1, result.TotalCount);
    }

    [Fact]
    public async Task DownloadQueueItemRepository_GetByRunId_ExcludesGroupLeavesAndReturnsVisibleGroupProgress()
    {
        var profileRepository = new SftpConnectionProfileRepository(_fixture.DataSource);
        var jobRepository = new SporeSyncJobRepository(_fixture.DataSource);
        var runRepository = new SporeSyncRunRepository(_fixture.DataSource);
        var queueRepository = new DownloadQueueItemRepository(_fixture.DataSource);

        var profile = await profileRepository.UpsertAsync(CreateProfile());
        var job = await jobRepository.UpsertAsync(CreateJob(profile.Id));
        var run = await runRepository.CreateAsync(job.Id);

        var group = await queueRepository.UpsertAsync(new UpsertDownloadQueueItem
        {
            JobId = job.Id,
            SyncRunId = run.Id,
            RemotePath = "/incoming/group/",
            DestinationPath = "/local/incoming/group",
            FileSizeBytes = 300,
            RemoteModifiedAt = DateTimeOffset.UtcNow,
            IsGroup = true,
            GroupRemotePath = null,
            ChildCount = 2
        });
        await queueRepository.UpsertAsync(new UpsertDownloadQueueItem
        {
            JobId = job.Id,
            SyncRunId = run.Id,
            RemotePath = "/incoming/group/a.csv",
            DestinationPath = "/local/incoming/group/a.csv",
            FileSizeBytes = 100,
            RemoteModifiedAt = DateTimeOffset.UtcNow,
            IsGroup = false,
            GroupRemotePath = group.RemotePath,
            ChildCount = 0
        });

        var visibleProgress = await queueRepository.UpdateProgressAsync(new UpdateDownloadQueueItemProgress
        {
            Id = group.Id,
            Status = "downloading",
            BytesDownloaded = 75
        });

        var result = await queueRepository.GetByRunIdAsync(run.Id, new QueueItemQuery
        {
            PageNumber = 1,
            PageSize = 10,
            SortBy = "queuedAt",
            SortDirection = "asc"
        });

        var item = Assert.Single(result.Items);
        Assert.Equal(group.Id, item.Id);
        Assert.True(item.IsGroup);
        Assert.Null(item.GroupRemotePath);
        Assert.Equal(75, visibleProgress.BytesDownloaded);
        Assert.Equal(75, item.BytesDownloaded);
        Assert.Equal(1, result.TotalCount);
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

    private async Task SeedRunAsync(
        Guid jobId,
        Guid runId,
        string status,
        string remotePath)
    {
        const string sql = """
            INSERT INTO core.sftp_sync_runs (
                id,
                job_id,
                status,
                total_file_count,
                completed_file_count,
                total_bytes,
                downloaded_bytes)
            VALUES (
                @run_id,
                @job_id,
                @status,
                1,
                0,
                100,
                50);

            -- Phase 3 note (plan:343): INSERT omits grouping columns (Phase 1 defaults false/NULL/0).
            -- Rows remain visible non-group (Phase 2 filter + rules.md:170 invariant #4). Group+leaf test data in later phases.
            INSERT INTO core.download_queue_items (
                id,
                job_id,
                sync_run_id,
                remote_path,
                destination_path,
                file_size_bytes,
                status,
                bytes_downloaded)
            VALUES (
                @queue_item_id,
                @job_id,
                @run_id,
                @remote_path,
                @destination_path,
                100,
                'downloading',
                50);
            """;

        await using var connection = await _fixture.DataSource.OpenConnectionAsync();
        await using var command = new Npgsql.NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("run_id", runId);
        command.Parameters.AddWithValue("job_id", jobId);
        command.Parameters.AddWithValue("status", status);
        command.Parameters.AddWithValue("queue_item_id", Guid.NewGuid());
        command.Parameters.AddWithValue("remote_path", remotePath);
        command.Parameters.AddWithValue("destination_path", $"/local{remotePath}");
        await command.ExecuteNonQueryAsync();
    }

    private async Task SeedQueueItemAsync(
        Guid jobId,
        Guid runId,
        string remotePath,
        string status,
        long fileSizeBytes,
        long bytesDownloaded)
    {
        const string sql = """
            INSERT INTO core.download_queue_items (
                id,
                job_id,
                sync_run_id,
                remote_path,
                destination_path,
                file_size_bytes,
                status,
                bytes_downloaded)
            VALUES (
                @queue_item_id,
                @job_id,
                @run_id,
                @remote_path,
                @destination_path,
                @file_size_bytes,
                @status,
                @bytes_downloaded);
            """;

        await using var connection = await _fixture.DataSource.OpenConnectionAsync();
        await using var command = new Npgsql.NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("queue_item_id", Guid.NewGuid());
        command.Parameters.AddWithValue("job_id", jobId);
        command.Parameters.AddWithValue("run_id", runId);
        command.Parameters.AddWithValue("remote_path", remotePath);
        command.Parameters.AddWithValue("destination_path", $"/local{remotePath}");
        command.Parameters.AddWithValue("file_size_bytes", fileSizeBytes);
        command.Parameters.AddWithValue("status", status);
        command.Parameters.AddWithValue("bytes_downloaded", bytesDownloaded);
        await command.ExecuteNonQueryAsync();
    }

    [Fact]
    public async Task WorkerRepositories_CreateRunClaimAndRequeue_WorkThroughPostgres()
    {
        var profileRepository = new SftpConnectionProfileRepository(_fixture.DataSource);
        var jobRepository = new SporeSyncJobRepository(_fixture.DataSource);
        var runRepository = new SporeSyncRunRepository(_fixture.DataSource);
        var queueRepository = new DownloadQueueItemRepository(_fixture.DataSource);

        var profile = await profileRepository.UpsertAsync(new SftpConnectionProfile
        {
            Id = Guid.NewGuid(),
            Name = $"profile-{Guid.NewGuid():N}",
            Host = "sftp.example.com",
            Port = 22,
            Username = "sync-user",
            EncryptedPassword = "encrypted-password",
            IsDefault = false
        });

        var job = await jobRepository.UpsertAsync(new UpsertSporeSyncJob
        {
            ConnectionProfileId = profile.Id,
            Name = $"job-{Guid.NewGuid():N}",
            SourcePath = "/remote/incoming",
            DestinationPath = "/data/incoming",
            PollingIntervalSeconds = 120,
            IsEnabled = true
        });

        Assert.False(await runRepository.HasActiveRunAsync(job.Id));
        var run = await runRepository.CreateAsync(job.Id, leaseSeconds: 300);
        Assert.NotNull(run);
        Assert.Equal("queued", run!.Status);
        Assert.True(await runRepository.HasActiveRunAsync(job.Id));

        var item = await queueRepository.UpsertAsync(new UpsertDownloadQueueItem
        {
            JobId = job.Id,
            SyncRunId = run.Id,
            RemotePath = "/remote/incoming/file.txt",
            DestinationPath = "/data/incoming/file.txt",
            FileSizeBytes = 100,
            RemoteModifiedAt = DateTimeOffset.UtcNow,
            IsGroup = false,
            ChildCount = 0
        });
        Assert.Equal("queued", item.Status);

        // Items only become claimable once the run finished its scan phase.
        await runRepository.UpdateStatusAsync(new UpdateSporeSyncRunStatus
        {
            Id = run.Id,
            Status = "downloading"
        });

        var claimed = await queueRepository.ClaimNextAsync(leaseSeconds: 300);
        Assert.NotNull(claimed);
        Assert.Equal("downloading", claimed!.Status);

        await queueRepository.UpdateProgressAsync(new UpdateDownloadQueueItemProgress
        {
            Id = claimed.Id,
            Status = "failed",
            BytesDownloaded = 0,
            ErrorMessage = "test failure"
        });

        var requeuedCount = await queueRepository.RequeueFailedAsync(job.Id, run.Id);
        Assert.Equal(1, requeuedCount);

        var syncedState = await queueRepository.GetSyncedStateAsync(job.Id);
        Assert.True(syncedState.ContainsKey("/remote/incoming/file.txt"));
        Assert.Equal("queued", syncedState["/remote/incoming/file.txt"].Status);
    }

    [Fact]
    public async Task DownloadQueueItemRepository_UpdateProgress_DoesNotRegressTerminalItemToDownloading()
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
            RemotePath = "/incoming/race.csv",
            DestinationPath = "/local/incoming/race.csv",
            FileSizeBytes = 100,
            RemoteModifiedAt = DateTimeOffset.UtcNow,
            IsGroup = false,
            ChildCount = 0
        });

        var completed = await queueRepository.UpdateProgressAsync(new UpdateDownloadQueueItemProgress
        {
            Id = item.Id,
            Status = "completed",
            BytesDownloaded = 100,
            CurrentBytesPerSecond = 25
        });

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            queueRepository.UpdateProgressAsync(new UpdateDownloadQueueItemProgress
            {
                Id = item.Id,
                Status = "downloading",
                BytesDownloaded = 50
            }));

        var afterLateProgress = await queueRepository.GetByIdAsync(item.Id);
        Assert.NotNull(afterLateProgress);
        Assert.Equal("completed", afterLateProgress.Status);
        Assert.Equal(completed.CompletedAt, afterLateProgress.CompletedAt);
        Assert.Equal(100, afterLateProgress.BytesDownloaded);
        Assert.Equal(25, afterLateProgress.CurrentBytesPerSecond);
    }

    [Fact]
    public async Task DownloadQueueItemRepository_MarkRemoteDeleted_SkipsCompletedVisibleItems()
    {
        var profileRepository = new SftpConnectionProfileRepository(_fixture.DataSource);
        var jobRepository = new SporeSyncJobRepository(_fixture.DataSource);
        var runRepository = new SporeSyncRunRepository(_fixture.DataSource);
        var queueRepository = new DownloadQueueItemRepository(_fixture.DataSource);

        var profile = await profileRepository.UpsertAsync(CreateProfile());
        var job = await jobRepository.UpsertAsync(CreateJob(profile.Id));
        var run = await runRepository.CreateAsync(job.Id, leaseSeconds: 300);
        Assert.NotNull(run);

        var item = await queueRepository.UpsertAsync(new UpsertDownloadQueueItem
        {
            JobId = job.Id,
            SyncRunId = run!.Id,
            RemotePath = "/incoming/deleted.csv",
            DestinationPath = "/local/incoming/deleted.csv",
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

        var marked = await queueRepository.MarkRemoteDeletedAsync(
            job.Id,
            run.Id,
            ["/incoming/deleted.csv"]);

        var skipped = Assert.Single(marked);
        Assert.Equal(item.Id, skipped.Id);
        Assert.Equal("skipped", skipped.Status);
        Assert.Equal("remote_deleted", skipped.HandledReason);
        Assert.Equal(0, skipped.BytesDownloaded);
    }

    [Fact]
    public async Task DownloadQueueItemRepository_RecordFailure_RequeuesWithBackoffUntilBudgetExhausted()
    {
        var queueRepository = new DownloadQueueItemRepository(_fixture.DataSource);
        var item = await SeedClaimableItemAsync(queueRepository, "/incoming/retry-me.csv");

        // First failure with budget remaining: requeued with a scheduled next attempt.
        var afterFirstFailure = await queueRepository.RecordFailureAsync(
            item.Id,
            "transient error",
            maxRetries: 1,
            nextAttemptAt: DateTimeOffset.UtcNow.AddMinutes(5));

        Assert.Equal("queued", afterFirstFailure.Status);
        Assert.Equal(1, afterFirstFailure.RetryCount);
        Assert.Equal("retry_scheduled", afterFirstFailure.HandledReason);
        Assert.Equal("transient error", afterFirstFailure.ErrorMessage);
        Assert.Null(afterFirstFailure.CompletedAt);

        // Second failure exhausts the budget: dead-lettered as terminal 'failed'.
        var afterSecondFailure = await queueRepository.RecordFailureAsync(
            item.Id,
            "still failing",
            maxRetries: 1,
            nextAttemptAt: DateTimeOffset.UtcNow.AddMinutes(5));

        Assert.Equal("failed", afterSecondFailure.Status);
        Assert.Equal(2, afterSecondFailure.RetryCount);
        Assert.Equal("retry_budget_exhausted", afterSecondFailure.HandledReason);
        Assert.NotNull(afterSecondFailure.CompletedAt);
    }

    [Fact]
    public async Task DownloadQueueItemRepository_ClaimNext_RespectsScheduledNextAttempt()
    {
        var queueRepository = new DownloadQueueItemRepository(_fixture.DataSource);
        var item = await SeedClaimableItemAsync(queueRepository, "/incoming/backoff.csv");

        // Fail with a future next attempt: not claimable yet.
        await queueRepository.RecordFailureAsync(
            item.Id,
            "transient error",
            maxRetries: 5,
            nextAttemptAt: DateTimeOffset.UtcNow.AddHours(1));

        var claimedTooEarly = await ClaimSpecificAsync(queueRepository, item.Id);
        Assert.Null(claimedTooEarly);

        // Fail with a past next attempt: immediately claimable again.
        await queueRepository.RecordFailureAsync(
            item.Id,
            "transient error",
            maxRetries: 5,
            nextAttemptAt: DateTimeOffset.UtcNow.AddHours(-1));

        var claimed = await ClaimSpecificAsync(queueRepository, item.Id);
        Assert.NotNull(claimed);
        Assert.Equal("downloading", claimed.Status);
    }

    [Fact]
    public async Task DownloadQueueItemRepository_Defer_RequeuesWithoutConsumingRetryBudget()
    {
        var queueRepository = new DownloadQueueItemRepository(_fixture.DataSource);
        var item = await SeedClaimableItemAsync(queueRepository, "/incoming/unstable.csv");

        var deferred = await queueRepository.DeferAsync(
            item.Id,
            DateTimeOffset.UtcNow.AddSeconds(30),
            "awaiting_remote_stability");

        Assert.Equal("queued", deferred.Status);
        Assert.Equal(0, deferred.RetryCount);
        Assert.Equal("awaiting_remote_stability", deferred.HandledReason);
    }

    [Fact]
    public async Task DownloadQueueItemRepository_Retry_ResetsDeadLetteredItemAndRejectsOthers()
    {
        var queueRepository = new DownloadQueueItemRepository(_fixture.DataSource);
        var item = await SeedClaimableItemAsync(queueRepository, "/incoming/dead.csv");

        // Dead-letter the item (budget 0: the first failure is terminal).
        var dead = await queueRepository.RecordFailureAsync(
            item.Id,
            "fatal",
            maxRetries: 0,
            nextAttemptAt: DateTimeOffset.UtcNow);
        Assert.Equal("failed", dead.Status);

        var retried = await queueRepository.RetryAsync(item.Id);

        Assert.NotNull(retried);
        Assert.Equal("queued", retried.Status);
        Assert.Equal(0, retried.RetryCount);
        Assert.Null(retried.ErrorMessage);
        Assert.Null(retried.HandledReason);
        Assert.Null(retried.CompletedAt);

        // Retrying a non-failed item returns null.
        var secondRetry = await queueRepository.RetryAsync(item.Id);
        Assert.Null(secondRetry);
    }

    [Fact]
    public async Task DownloadQueueItemRepository_UpsertAfterDeadLetter_ResetsRetryBudget()
    {
        var profileRepository = new SftpConnectionProfileRepository(_fixture.DataSource);
        var jobRepository = new SporeSyncJobRepository(_fixture.DataSource);
        var runRepository = new SporeSyncRunRepository(_fixture.DataSource);
        var queueRepository = new DownloadQueueItemRepository(_fixture.DataSource);

        var profile = await profileRepository.UpsertAsync(CreateProfile());
        var job = await jobRepository.UpsertAsync(CreateJob(profile.Id));
        var run = await runRepository.CreateAsync(job.Id);

        var upsert = new UpsertDownloadQueueItem
        {
            JobId = job.Id,
            SyncRunId = run.Id,
            RemotePath = "/incoming/changed.csv",
            DestinationPath = "/local/incoming/changed.csv",
            FileSizeBytes = 100,
            RemoteModifiedAt = DateTimeOffset.UtcNow,
            IsGroup = false,
            ChildCount = 0
        };
        var item = await queueRepository.UpsertAsync(upsert);

        var dead = await queueRepository.RecordFailureAsync(
            item.Id,
            "fatal",
            maxRetries: 0,
            nextAttemptAt: DateTimeOffset.UtcNow);
        Assert.Equal("failed", dead.Status);
        Assert.Equal(1, dead.RetryCount);

        // Remote content changed → the scan re-upserts and the budget starts fresh.
        var reEnqueued = await queueRepository.UpsertAsync(upsert);

        Assert.Equal(item.Id, reEnqueued.Id);
        Assert.Equal("queued", reEnqueued.Status);
        Assert.Equal(0, reEnqueued.RetryCount);
        Assert.Null(reEnqueued.ErrorMessage);
        Assert.Null(reEnqueued.HandledReason);
    }

    private async Task<DownloadQueueItem> SeedClaimableItemAsync(
        DownloadQueueItemRepository queueRepository,
        string remotePath)
    {
        var profileRepository = new SftpConnectionProfileRepository(_fixture.DataSource);
        var jobRepository = new SporeSyncJobRepository(_fixture.DataSource);
        var runRepository = new SporeSyncRunRepository(_fixture.DataSource);

        var profile = await profileRepository.UpsertAsync(CreateProfile());
        var job = await jobRepository.UpsertAsync(CreateJob(profile.Id));
        var run = await runRepository.CreateAsync(job.Id);
        run = await runRepository.UpdateStatusAsync(new UpdateSporeSyncRunStatus
        {
            Id = run.Id,
            Status = "downloading"
        });

        return await queueRepository.UpsertAsync(new UpsertDownloadQueueItem
        {
            JobId = job.Id,
            SyncRunId = run.Id,
            RemotePath = remotePath,
            DestinationPath = $"/local{remotePath}",
            FileSizeBytes = 100,
            RemoteModifiedAt = DateTimeOffset.UtcNow,
            IsGroup = false,
            ChildCount = 0
        });
    }

    /// <summary>
    /// Drains the shared claim queue looking for a specific item (other tests may have seeded
    /// claimable rows in the shared container). Returns null when the item was not claimable.
    /// </summary>
    private static async Task<DownloadQueueItem?> ClaimSpecificAsync(
        DownloadQueueItemRepository queueRepository,
        Guid itemId)
    {
        for (var i = 0; i < 50; i++)
        {
            var claimed = await queueRepository.ClaimNextAsync(leaseSeconds: 1800);
            if (claimed is null)
            {
                return null;
            }

            if (claimed.Id == itemId)
            {
                return claimed;
            }
        }

        return null;
    }
}
