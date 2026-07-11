using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Npgsql;
using SporeSync.Business;
using SporeSync.Business.Interface;
using SporeSync.Business.Worker;
using SporeSync.Domain.Interface;
using SporeSync.Domain.Model;
using SporeSync.Infrastructure.Repository;

namespace SporeSync.Business.Tests;

/// <summary>
/// Integration tests (Testcontainers + real Postgres) for the reliability follow-ups:
/// queue-item leasing / crash recovery sweeps, and the enqueue/claim + run-creation
/// race fixes.
/// </summary>
public sealed class ReliabilityIntegrationTests : IClassFixture<RepositoryTestcontainerFixture>
{
    private const int LeaseSeconds = 300;

    private readonly RepositoryTestcontainerFixture _fixture;
    private readonly SftpConnectionProfileRepository _profileRepository;
    private readonly SporeSyncJobRepository _jobRepository;
    private readonly SporeSyncRunRepository _runRepository;
    private readonly DownloadQueueItemRepository _queueRepository;

    public ReliabilityIntegrationTests(RepositoryTestcontainerFixture fixture)
    {
        _fixture = fixture;
        _profileRepository = new SftpConnectionProfileRepository(fixture.DataSource);
        _jobRepository = new SporeSyncJobRepository(fixture.DataSource);
        _runRepository = new SporeSyncRunRepository(fixture.DataSource);
        _queueRepository = new DownloadQueueItemRepository(fixture.DataSource);
    }

    [Fact]
    public async Task ClaimNext_OnlyClaimsItems_WhoseRunFinishedScanning()
    {
        var job = await CreateJobAsync();
        var run = await CreateRunAsync(job.Id);
        var item = await UpsertItemAsync(job.Id, run.Id, "/incoming/gated.bin");

        // Run is 'queued' (scan/enqueue not finished): nothing claimable.
        Assert.Null(await ClaimForJobAsync(job.Id));

        await _runRepository.UpdateStatusAsync(new UpdateSporeSyncRunStatus
        {
            Id = run.Id,
            Status = "scanning"
        });
        Assert.Null(await ClaimForJobAsync(job.Id));

        await _runRepository.UpdateStatusAsync(new UpdateSporeSyncRunStatus
        {
            Id = run.Id,
            Status = "downloading"
        });

        var claimed = await ClaimForJobAsync(job.Id);
        Assert.NotNull(claimed);
        Assert.Equal(item.Id, claimed!.Id);
        Assert.Equal("downloading", claimed.Status);
    }

    [Fact]
    public async Task TryCreateRun_ConcurrentAttemptsCreateExactlyOneActiveRun()
    {
        var job = await CreateJobAsync();

        var attempts = Enumerable.Range(0, 20)
            .Select(_ => new SporeSyncRunRepository(_fixture.DataSource)
                .TryCreateAsync(job.Id, LeaseSeconds));
        var results = await Task.WhenAll(attempts);

        var first = Assert.Single(results, run => run is not null)!;
        Assert.Equal(19, results.Count(run => run is null));

        await using var connection = await _fixture.DataSource.OpenConnectionAsync();
        await using var countCommand = new NpgsqlCommand("""
            SELECT count(*)
            FROM core.sftp_sync_runs
            WHERE job_id = @job_id
              AND status IN ('queued', 'scanning', 'downloading');
            """, connection);
        countCommand.Parameters.AddWithValue("job_id", job.Id);
        var activeRunCount = (long)(await countCommand.ExecuteScalarAsync()
            ?? throw new InvalidOperationException("Active run count query returned no value."));
        Assert.Equal(1L, activeRunCount);

        await _runRepository.UpdateStatusAsync(new UpdateSporeSyncRunStatus
        {
            Id = first.Id,
            Status = "completed"
        });

        var third = await _runRepository.TryCreateAsync(job.Id, LeaseSeconds);
        Assert.NotNull(third);
    }

    [Fact]
    public async Task RequeueStale_RequeuesOnlyExpiredLeases_AndIncrementsRetryCount()
    {
        var job = await CreateJobAsync();
        var run = await CreateRunAsync(job.Id, status: "downloading");
        var item = await UpsertItemAsync(job.Id, run.Id, "/incoming/stale.bin");

        var claimed = await ClaimForJobAsync(job.Id);
        Assert.NotNull(claimed);

        // Lease is still valid: the periodic sweep must not touch the item.
        var untouched = await _queueRepository.RequeueStaleAsync(ignoreLeases: false);
        Assert.DoesNotContain(untouched, requeued => requeued.Id == item.Id);

        await BackdateItemLeaseAsync(item.Id);

        var requeued = await _queueRepository.RequeueStaleAsync(ignoreLeases: false);
        var recovered = Assert.Single(requeued, r => r.Id == item.Id);
        Assert.Equal("queued", recovered.Status);
        Assert.Equal(claimed!.RetryCount + 1, recovered.RetryCount);
        Assert.Equal(0, recovered.BytesDownloaded);
        Assert.Null(recovered.ErrorMessage);
        Assert.Null(recovered.StartedAt);
    }

    [Fact]
    public async Task RequeueStale_IgnoringLeases_RequeuesActiveClaims()
    {
        var job = await CreateJobAsync();
        var run = await CreateRunAsync(job.Id, status: "downloading");
        var item = await UpsertItemAsync(job.Id, run.Id, "/incoming/startup.bin");

        Assert.NotNull(await ClaimForJobAsync(job.Id));

        var requeued = await _queueRepository.RequeueStaleAsync(ignoreLeases: true);
        var recovered = Assert.Single(requeued, r => r.Id == item.Id);
        Assert.Equal("queued", recovered.Status);
    }

    [Fact]
    public async Task Release_ReturnsClaimedItemToQueue_WithoutRecordingFailure()
    {
        var job = await CreateJobAsync();
        var run = await CreateRunAsync(job.Id, status: "downloading");
        var item = await UpsertItemAsync(job.Id, run.Id, "/incoming/graceful.bin");

        Assert.NotNull(await ClaimForJobAsync(job.Id));

        var released = await _queueRepository.ReleaseAsync(item.Id);
        Assert.NotNull(released);
        Assert.Equal("queued", released!.Status);
        Assert.Null(released.ErrorMessage);
        Assert.Equal(item.RetryCount, released.RetryCount);

        // Releasing an item that is not claimed is a no-op.
        Assert.Null(await _queueRepository.ReleaseAsync(item.Id));
    }

    [Fact]
    public async Task RenewLease_ExtendsLease_OnlyWhileDownloading()
    {
        var job = await CreateJobAsync();
        var run = await CreateRunAsync(job.Id, status: "downloading");
        var item = await UpsertItemAsync(job.Id, run.Id, "/incoming/renewed.bin");

        Assert.False(await _queueRepository.RenewLeaseAsync(item.Id, LeaseSeconds));

        Assert.NotNull(await ClaimForJobAsync(job.Id));
        await BackdateItemLeaseAsync(item.Id);

        Assert.True(await _queueRepository.RenewLeaseAsync(item.Id, LeaseSeconds));

        // The renewed lease protects the item from the periodic sweep.
        var requeued = await _queueRepository.RequeueStaleAsync(ignoreLeases: false);
        Assert.DoesNotContain(requeued, r => r.Id == item.Id);
    }

    [Fact]
    public async Task ReapOrphanedRuns_FailsScanningRuns_WithExpiredLeases()
    {
        var job = await CreateJobAsync();
        var run = await CreateRunAsync(job.Id);
        await _runRepository.UpdateStatusAsync(new UpdateSporeSyncRunStatus
        {
            Id = run.Id,
            Status = "scanning",
            LeaseSeconds = LeaseSeconds
        });

        // Lease still valid: run must survive the periodic sweep.
        var untouched = await _runRepository.ReapOrphanedAsync(ignoreLeases: false);
        Assert.DoesNotContain(untouched, reaped => reaped.Id == run.Id);

        await BackdateRunLeaseAsync(run.Id);

        var reapedRuns = await _runRepository.ReapOrphanedAsync(ignoreLeases: false);
        var reaped = Assert.Single(reapedRuns, r => r.Id == run.Id);
        Assert.Equal("failed", reaped.Status);
        Assert.NotNull(reaped.ErrorMessage);
        Assert.NotNull(reaped.CompletedAt);
    }

    [Fact]
    public async Task RenewRunLease_ProtectsLongScanningRun_FromPeriodicRecoverySweep()
    {
        var job = await CreateJobAsync();
        var run = await CreateRunAsync(job.Id);
        await _runRepository.UpdateStatusAsync(new UpdateSporeSyncRunStatus
        {
            Id = run.Id,
            Status = "scanning",
            LeaseSeconds = LeaseSeconds
        });

        await BackdateRunLeaseAsync(run.Id);

        Assert.True(await _runRepository.RenewLeaseAsync(run.Id, LeaseSeconds));

        var service = new QueueRecoveryHostedService(
            BuildScopeFactory(),
            Options.Create(new SporeSyncOptions()),
            NullLogger<QueueRecoveryHostedService>.Instance);

        await service.SweepAsync(ignoreLeases: false, CancellationToken.None);

        var persisted = await _runRepository.GetByIdAsync(run.Id);
        Assert.Equal("scanning", persisted!.Status);
        Assert.Null(persisted.CompletedAt);
    }

    [Fact]
    public async Task RenewRunLease_ReturnsFalse_AfterRunLeavesQueuedOrScanning()
    {
        var job = await CreateJobAsync();
        var run = await CreateRunAsync(job.Id, status: "downloading");

        Assert.False(await _runRepository.RenewLeaseAsync(run.Id, LeaseSeconds));
    }

    [Fact]
    public async Task ReapOrphanedRuns_CompletesDownloadingRuns_WithNoPendingItems()
    {
        var job = await CreateJobAsync();
        var run = await CreateRunAsync(job.Id, status: "downloading");
        var item = await UpsertItemAsync(job.Id, run.Id, "/incoming/finished.bin");

        var claimed = await ClaimForJobAsync(job.Id);
        Assert.NotNull(claimed);

        // Simulate a worker that finished the last item but died before finalizing the run.
        await _queueRepository.UpdateProgressAsync(new UpdateDownloadQueueItemProgress
        {
            Id = item.Id,
            Status = "completed",
            BytesDownloaded = 100
        });

        var reapedRuns = await _runRepository.ReapOrphanedAsync(ignoreLeases: false);
        var reaped = Assert.Single(reapedRuns, r => r.Id == run.Id);
        Assert.Equal("completed", reaped.Status);
        Assert.Equal(1, reaped.CompletedFileCount);
        Assert.Equal(100, reaped.DownloadedBytes);
    }

    [Fact]
    public async Task UpdateStatus_DoesNotResurrectTerminalRuns()
    {
        var job = await CreateJobAsync();
        var run = await CreateRunAsync(job.Id);

        await _runRepository.UpdateStatusAsync(new UpdateSporeSyncRunStatus
        {
            Id = run.Id,
            Status = "cancelled"
        });

        var result = await _runRepository.UpdateStatusAsync(new UpdateSporeSyncRunStatus
        {
            Id = run.Id,
            Status = "downloading"
        });

        Assert.Equal("cancelled", result.Status);

        var persisted = await _runRepository.GetByIdAsync(run.Id);
        Assert.Equal("cancelled", persisted!.Status);
    }

    [Fact]
    public async Task UpsertMany_IsTransactional_NothingPersistsWhenOneItemFails()
    {
        var job = await CreateJobAsync();
        var run = await CreateRunAsync(job.Id);

        await Assert.ThrowsAsync<PostgresException>(() => _queueRepository.UpsertManyAsync(
            [
                NewUpsert(job.Id, run.Id, "/incoming/group", isGroup: true, childCount: 1),
                NewUpsert(job.Id, run.Id, "/incoming/group/leaf.bin", groupRemotePath: "/incoming/group", fileSizeBytes: -1)
            ]));

        var syncedState = await _queueRepository.GetSyncedStateAsync(job.Id);
        Assert.Empty(syncedState);
    }

    [Fact]
    public async Task UpsertMany_PersistsGroupsAndLeavesTogether()
    {
        var job = await CreateJobAsync();
        var run = await CreateRunAsync(job.Id);

        var items = await _queueRepository.UpsertManyAsync(
            [
                NewUpsert(job.Id, run.Id, "/incoming/show", isGroup: true, childCount: 2),
                NewUpsert(job.Id, run.Id, "/incoming/show/e1.mkv", groupRemotePath: "/incoming/show"),
                NewUpsert(job.Id, run.Id, "/incoming/show/e2.mkv", groupRemotePath: "/incoming/show")
            ]);

        Assert.Equal(3, items.Count);
        Assert.All(items, item => Assert.Equal("queued", item.Status));

        var leaves = await _queueRepository.GetLeavesForGroupAsync(run.Id, "/incoming/show");
        Assert.Equal(2, leaves.Count);
    }

    [Fact]
    public async Task RecoverySweep_RequeuesStaleItems_AndReapsOrphanedRuns()
    {
        var job = await CreateJobAsync();
        var run = await CreateRunAsync(job.Id, status: "downloading");
        var item = await UpsertItemAsync(job.Id, run.Id, "/incoming/sweep.bin");

        var claimed = await ClaimForJobAsync(job.Id);
        Assert.NotNull(claimed);

        var orphanedScanJob = await CreateJobAsync();
        var orphanedScanRun = await CreateRunAsync(orphanedScanJob.Id, status: "scanning");

        // Simulate a process crash: startup sweep runs with ignoreLeases: true.
        var service = new QueueRecoveryHostedService(
            BuildScopeFactory(),
            Options.Create(new SporeSyncOptions()),
            NullLogger<QueueRecoveryHostedService>.Instance);

        await service.SweepAsync(ignoreLeases: true, CancellationToken.None);

        var recoveredItem = await _queueRepository.GetByIdAsync(item.Id);
        Assert.Equal("queued", recoveredItem!.Status);
        Assert.Equal(claimed!.RetryCount + 1, recoveredItem.RetryCount);

        var reapedRun = await _runRepository.GetByIdAsync(orphanedScanRun.Id);
        Assert.Equal("failed", reapedRun!.Status);

        // The interrupted download run keeps its requeued item and stays claimable.
        var downloadRun = await _runRepository.GetByIdAsync(run.Id);
        Assert.Equal("downloading", downloadRun!.Status);
        var reclaimed = await ClaimForJobAsync(job.Id);
        Assert.NotNull(reclaimed);
        Assert.Equal(item.Id, reclaimed!.Id);
    }

    private IServiceScopeFactory BuildScopeFactory()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IDownloadQueueItemRepository>(_queueRepository);
        services.AddSingleton<ISporeSyncRunRepository>(_runRepository);
        services.AddSingleton<ISyncDashboardNotifier, NoopNotifier>();

        return services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>();
    }

    private sealed class NoopNotifier : ISyncDashboardNotifier
    {
        public Task NotifyRunUpdatedAsync(SporeSyncRun run, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task NotifyQueueItemUpdatedAsync(DownloadQueueItem item, CancellationToken cancellationToken = default)
            => Task.CompletedTask;
    }

    private async Task<SporeSyncJob> CreateJobAsync()
    {
        var profile = await _profileRepository.UpsertAsync(new SftpConnectionProfile
        {
            Id = Guid.NewGuid(),
            Name = $"profile-{Guid.NewGuid():N}",
            Host = "sftp.example.com",
            Port = 22,
            Username = "sync-user",
            EncryptedPassword = "encrypted-password",
            IsDefault = false
        });

        return await _jobRepository.UpsertAsync(new UpsertSporeSyncJob
        {
            ConnectionProfileId = profile.Id,
            Name = $"job-{Guid.NewGuid():N}",
            SourcePath = "/incoming",
            DestinationPath = "/local/incoming",
            PollingIntervalSeconds = 120,
            IsEnabled = true
        });
    }

    private async Task<SporeSyncRun> CreateRunAsync(Guid jobId, string? status = null)
    {
        var run = await _runRepository.TryCreateAsync(jobId, LeaseSeconds);
        Assert.NotNull(run);

        if (status is not null)
        {
            run = await _runRepository.UpdateStatusAsync(new UpdateSporeSyncRunStatus
            {
                Id = run!.Id,
                Status = status
            });
        }

        return run!;
    }

    private async Task<DownloadQueueItem> UpsertItemAsync(Guid jobId, Guid runId, string remotePath)
    {
        return await _queueRepository.UpsertAsync(NewUpsert(jobId, runId, remotePath));
    }

    private static UpsertDownloadQueueItem NewUpsert(
        Guid jobId,
        Guid runId,
        string remotePath,
        bool isGroup = false,
        string? groupRemotePath = null,
        int childCount = 0,
        long fileSizeBytes = 100)
    {
        return new UpsertDownloadQueueItem
        {
            JobId = jobId,
            SyncRunId = runId,
            RemotePath = remotePath,
            DestinationPath = $"/local{remotePath}",
            FileSizeBytes = fileSizeBytes,
            RemoteModifiedAt = DateTimeOffset.UtcNow,
            IsGroup = isGroup,
            GroupRemotePath = groupRemotePath,
            ChildCount = childCount
        };
    }

    /// <summary>
    /// Claims items until one belonging to the given job is returned (the container
    /// is shared by all tests in this class, so claimable leftovers from other tests
    /// are drained first). Returns null once nothing is claimable.
    /// </summary>
    private async Task<DownloadQueueItem?> ClaimForJobAsync(Guid jobId)
    {
        while (true)
        {
            var claimed = await _queueRepository.ClaimNextAsync(LeaseSeconds);
            if (claimed is null)
            {
                return null;
            }

            if (claimed.JobId == jobId)
            {
                return claimed;
            }
        }
    }

    private async Task BackdateItemLeaseAsync(Guid itemId)
    {
        const string sql = """
            UPDATE core.download_queue_items
            SET lease_expires_at = now() - interval '1 minute'
            WHERE id = @id;
            """;

        await using var connection = await _fixture.DataSource.OpenConnectionAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("id", itemId);
        await command.ExecuteNonQueryAsync();
    }

    private async Task BackdateRunLeaseAsync(Guid runId)
    {
        const string sql = """
            UPDATE core.sftp_sync_runs
            SET lease_expires_at = now() - interval '1 minute'
            WHERE id = @id;
            """;

        await using var connection = await _fixture.DataSource.OpenConnectionAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("id", runId);
        await command.ExecuteNonQueryAsync();
    }
}
