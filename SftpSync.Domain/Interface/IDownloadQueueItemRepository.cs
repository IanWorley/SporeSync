using SftpSync.Domain.Model;

namespace SftpSync.Domain.Interface;

public interface IDownloadQueueItemRepository
{
    Task<PagedResult<DownloadQueueItem>> GetByRunIdAsync(
        Guid runId,
        QueueItemQuery query,
        CancellationToken cancellationToken = default);

    // Phase 3 addition (plan:341): for future worker to load a group's internal leaves for requeue/resume.
    // Uses the Phase 2 internal SQL helper. Never exposed to UI paths (enforces no leaf leakage).
    Task<IReadOnlyList<DownloadQueueItem>> GetLeavesForGroupAsync(
        Guid runId,
        string groupRemotePath,
        CancellationToken cancellationToken = default);
}
