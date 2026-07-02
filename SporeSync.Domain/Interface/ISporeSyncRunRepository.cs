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

    Task<SporeSyncRun> CreateAsync(Guid jobId, CancellationToken cancellationToken = default);

    Task<SporeSyncRun> UpdateStatusAsync(
        UpdateSporeSyncRunStatus update,
        CancellationToken cancellationToken = default);

    Task<bool> HasActiveRunAsync(Guid jobId, CancellationToken cancellationToken = default);

    Task<SporeSyncRun> RecalculateAggregatesAsync(
        Guid runId,
        CancellationToken cancellationToken = default);

    Task<bool> HasPendingDownloadsAsync(Guid runId, CancellationToken cancellationToken = default);

    Task<SyncHistoryPruneResult> PruneHistoryAsync(
        DateTimeOffset cutoff,
        CancellationToken cancellationToken = default);
}
