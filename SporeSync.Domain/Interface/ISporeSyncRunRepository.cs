using SporeSync.Domain.Model;

namespace SporeSync.Domain.Interface;

public interface ISporeSyncRunRepository
{
    Task<PagedResult<SporeSyncRun>> GetRunsAsync(
        RunQuery query,
        CancellationToken cancellationToken = default);

    Task<SporeSyncRun?> GetByIdAsync(
        Guid id,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Atomically creates a run for the job, or returns <c>null</c> when the job
    /// already has an active (queued/scanning/downloading) run.
    /// </summary>
    Task<SporeSyncRun?> CreateAsync(
        Guid jobId,
        int leaseSeconds = 1800,
        CancellationToken cancellationToken = default);

    Task<SporeSyncRun> UpdateStatusAsync(
        UpdateSporeSyncRunStatus update,
        CancellationToken cancellationToken = default);

    Task<bool> HasActiveRunAsync(Guid jobId, CancellationToken cancellationToken = default);

    Task<SporeSyncRun> RecalculateAggregatesAsync(
        Guid runId,
        CancellationToken cancellationToken = default);

    Task<bool> HasPendingDownloadsAsync(Guid runId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Renews an active queued/scanning run lease. Returns <c>false</c> when the
    /// run is missing or no longer in a lease-renewable state.
    /// </summary>
    Task<bool> RenewLeaseAsync(
        Guid runId,
        int leaseSeconds,
        CancellationToken cancellationToken = default);

    Task<SyncHistoryPruneResult> PruneHistoryAsync(
        DateTimeOffset cutoff,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Reaps orphaned runs: queued/scanning runs whose lease expired are marked
    /// failed, and downloading runs with no pending items are finalized as
    /// completed. Returns every run that was mutated.
    /// </summary>
    Task<IReadOnlyList<SporeSyncRun>> ReapOrphanedAsync(
        bool ignoreLeases,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Transitions the run to downloading and requeues its failed items in one
    /// database operation. Returns the number of UI-visible items requeued.
    /// </summary>
    Task<int> RetryFailedItemsAsync(Guid runId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Applies a scan status transition only when the run is still in
    /// <paramref name="expectedStatus"/>. When the run was cancelled mid-scan the
    /// transition is skipped, any items enqueued after the cancellation are marked
    /// skipped, and the run is returned unchanged (still cancelled).
    /// </summary>
    Task<SporeSyncRun> AdvanceScanStatusAsync(
        UpdateSporeSyncRunStatus update,
        string expectedStatus,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Cancels an active run (queued/scanning/downloading) and skips its pending
    /// queue items. Returns null when the run does not exist or is not active.
    /// </summary>
    Task<SporeSyncRun?> CancelAsync(Guid runId, CancellationToken cancellationToken = default);
}
