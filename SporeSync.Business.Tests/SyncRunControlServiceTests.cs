using SporeSync.Business.Interface;
using SporeSync.Business.Service;
using SporeSync.Domain.Interface;
using SporeSync.Domain.Model;

namespace SporeSync.Business.Tests;

public sealed class SyncRunControlServiceTests
{
    [Fact]
    public async Task CancelRunAsync_ReturnsNotFound_WhenRunDoesNotExist()
    {
        var runRepository = new FakeRunRepository { RunById = null };
        var service = CreateService(runRepository, out var notifier);

        var result = await service.CancelRunAsync(Guid.NewGuid());

        Assert.Equal(SyncRunControlError.NotFound, result.Error);
        Assert.Empty(notifier.RunUpdates);
    }

    [Fact]
    public async Task CancelRunAsync_ReturnsNotActive_WhenRunIsTerminal()
    {
        var run = CreateRun("completed");
        var runRepository = new FakeRunRepository { RunById = run, CancelResult = null };
        var service = CreateService(runRepository, out var notifier);

        var result = await service.CancelRunAsync(run.Id);

        Assert.Equal(SyncRunControlError.NotActive, result.Error);
        Assert.Empty(notifier.RunUpdates);
    }

    [Fact]
    public async Task CancelRunAsync_CancelsRecalculatesAndNotifies()
    {
        var run = CreateRun("downloading");
        var cancelled = CreateRun("cancelled", run.Id, run.JobId);
        var runRepository = new FakeRunRepository
        {
            RunById = run,
            CancelResult = cancelled,
            RecalculateResult = cancelled
        };
        var service = CreateService(runRepository, out var notifier);

        var result = await service.CancelRunAsync(run.Id);

        Assert.Null(result.Error);
        Assert.Equal("cancelled", result.Run!.Status);
        Assert.Equal(run.Id, runRepository.RecalculatedRunId);
        var notified = Assert.Single(notifier.RunUpdates);
        Assert.Equal("cancelled", notified.Status);
    }

    [Fact]
    public async Task RetryFailedItemsAsync_ReturnsNoFailedItems_WhenNothingToRetry()
    {
        var run = CreateRun("failed");
        var runRepository = new FakeRunRepository { RunById = run, RetryCount = 0 };
        var service = CreateService(runRepository, out var notifier);

        var result = await service.RetryFailedItemsAsync(run.Id);

        Assert.Equal(SyncRunControlError.NoFailedItems, result.Error);
        Assert.Empty(notifier.RunUpdates);
    }

    [Fact]
    public async Task RetryFailedItemsAsync_RequeuesItemsMarksRunDownloadingAndNotifies()
    {
        var run = CreateRun("failed");
        var downloading = CreateRun("downloading", run.Id, run.JobId);
        var runRepository = new FakeRunRepository
        {
            RunById = run,
            RetryCount = 3,
            RecalculateResult = downloading
        };
        var service = CreateService(runRepository, out var notifier);

        var result = await service.RetryFailedItemsAsync(run.Id);

        Assert.Null(result.Error);
        Assert.Equal(3, result.RetriedCount);
        Assert.Equal("downloading", result.Run!.Status);
        Assert.Null(runRepository.LastStatusUpdate);
        Assert.Equal(run.Id, runRepository.RetriedRunId);
        Assert.Single(notifier.RunUpdates);
    }

    private static SyncRunControlService CreateService(
        FakeRunRepository runRepository,
        out RecordingNotifier notifier)
    {
        notifier = new RecordingNotifier();
        return new SyncRunControlService(runRepository, notifier);
    }

    private static SporeSyncRun CreateRun(string status, Guid? id = null, Guid? jobId = null)
    {
        return new SporeSyncRun
        {
            Id = id ?? Guid.NewGuid(),
            JobId = jobId ?? Guid.NewGuid(),
            JobName = "job",
            Status = status,
            StartedAt = DateTimeOffset.UtcNow,
            TotalFileCount = 3,
            CompletedFileCount = 1,
            SkippedFileCount = 0,
            FailedFileCount = 2,
            TotalBytes = 300,
            DownloadedBytes = 100
        };
    }

    private sealed class RecordingNotifier : ISyncDashboardNotifier
    {
        public List<SporeSyncRun> RunUpdates { get; } = [];

        public Task NotifyRunUpdatedAsync(SporeSyncRun run, CancellationToken cancellationToken = default)
        {
            RunUpdates.Add(run);
            return Task.CompletedTask;
        }

        public Task NotifyQueueItemUpdatedAsync(DownloadQueueItem item, CancellationToken cancellationToken = default)
            => Task.CompletedTask;
    }

    private sealed class FakeRunRepository : ISporeSyncRunRepository
    {
        public SporeSyncRun? RunById { get; init; }

        public SporeSyncRun? CancelResult { get; init; }

        public SporeSyncRun? RecalculateResult { get; init; }

        public int RetryCount { get; init; }

        public Guid? RecalculatedRunId { get; private set; }

        public Guid? RetriedRunId { get; private set; }

        public UpdateSporeSyncRunStatus? LastStatusUpdate { get; private set; }

        public Task<SporeSyncRun?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
            => Task.FromResult(RunById);

        public Task<SporeSyncRun?> CancelAsync(Guid runId, CancellationToken cancellationToken = default)
            => Task.FromResult(CancelResult);

        public Task<int> RetryFailedItemsAsync(Guid runId, CancellationToken cancellationToken = default)
        {
            RetriedRunId = runId;
            return Task.FromResult(RetryCount);
        }

        public Task<SporeSyncRun> RecalculateAggregatesAsync(Guid runId, CancellationToken cancellationToken = default)
        {
            RecalculatedRunId = runId;
            return Task.FromResult(RecalculateResult ?? throw new InvalidOperationException("RecalculateResult not set."));
        }

        public Task<SporeSyncRun> UpdateStatusAsync(UpdateSporeSyncRunStatus update, CancellationToken cancellationToken = default)
        {
            LastStatusUpdate = update;
            return Task.FromResult(RecalculateResult ?? throw new InvalidOperationException("RecalculateResult not set."));
        }

        public Task<PagedResult<SporeSyncRun>> GetRunsAsync(RunQuery query, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<SporeSyncRun?> TryCreateAsync(
            Guid jobId,
            int leaseSeconds = 1800,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<bool> HasActiveRunAsync(Guid jobId, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<bool> HasPendingDownloadsAsync(Guid runId, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<bool> RenewLeaseAsync(Guid runId, int leaseSeconds, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<SporeSyncRun> AdvanceScanStatusAsync(UpdateSporeSyncRunStatus update, string expectedStatus, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<SyncHistoryPruneResult> PruneHistoryAsync(DateTimeOffset cutoff, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<IReadOnlyList<SporeSyncRun>> ReapOrphanedAsync(
            bool ignoreLeases,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
    }
}
