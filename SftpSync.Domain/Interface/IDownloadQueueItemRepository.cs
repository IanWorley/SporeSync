using SftpSync.Domain.Model;

namespace SftpSync.Domain.Interface;

public interface IDownloadQueueItemRepository
{
    Task<PagedResult<DownloadQueueItem>> GetByRunIdAsync(
        Guid runId,
        QueueItemQuery query,
        CancellationToken cancellationToken = default);
}
