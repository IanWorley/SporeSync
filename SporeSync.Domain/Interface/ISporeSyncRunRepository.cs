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
}
