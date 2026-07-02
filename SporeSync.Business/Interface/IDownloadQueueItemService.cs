using SporeSync.Domain.Model;

namespace SporeSync.Business.Interface;

public interface IDownloadQueueItemService
{
    Task<PagedResult<DownloadQueueItem>> GetByRunIdAsync(
        Guid runId,
        QueueItemQuery query,
        CancellationToken cancellationToken = default);

    // Phase 3 (plan:341): worker subtree loader (delegates to repo; internal only).
    Task<IReadOnlyList<DownloadQueueItem>> GetLeavesForGroupAsync(
        Guid runId,
        string groupRemotePath,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Manually requeues a dead-lettered ('failed') queue item with a fresh retry budget.
    /// </summary>
    Task<RetryQueueItemResult> RetryAsync(
        Guid runId,
        Guid queueItemId,
        CancellationToken cancellationToken = default);
}

public enum RetryQueueItemStatus
{
    NotFound,
    NotRetryable,
    Retried
}

public sealed record RetryQueueItemResult(
    RetryQueueItemStatus Status,
    DownloadQueueItem? Item);
