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
}
