using SporeSync.Domain.Model;

namespace SporeSync.Business.Interface;

public enum SyncRunControlError
{
    NotFound,
    NotActive,
    NoFailedItems
}

public sealed class SyncRunControlResult
{
    public SporeSyncRun? Run { get; init; }

    public SyncRunControlError? Error { get; init; }

    public int RetriedCount { get; init; }
}

public interface ISyncRunControlService
{
    Task<SyncRunControlResult> CancelRunAsync(
        Guid runId,
        CancellationToken cancellationToken = default);

    Task<SyncRunControlResult> RetryFailedItemsAsync(
        Guid runId,
        CancellationToken cancellationToken = default);
}
