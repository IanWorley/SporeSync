using SftpSync.Domain.Model;

namespace SftpSync.Business.Interface;

public interface IDownloadQueueItemService
{
    Task<PagedResult<DownloadQueueItem>> GetByRunIdAsync(
        Guid runId,
        QueueItemQuery query,
        CancellationToken cancellationToken = default);
}
