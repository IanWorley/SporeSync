using SftpSync.Business.Interface;
using SftpSync.Domain.Interface;
using SftpSync.Domain.Model;

namespace SftpSync.Business.Service;

public sealed class DownloadQueueItemService : IDownloadQueueItemService
{
    private readonly IDownloadQueueItemRepository _repository;

    public DownloadQueueItemService(IDownloadQueueItemRepository repository)
    {
        _repository = repository;
    }

    public Task<PagedResult<DownloadQueueItem>> GetByRunIdAsync(
        Guid runId,
        QueueItemQuery query,
        CancellationToken cancellationToken = default)
    {
        return _repository.GetByRunIdAsync(runId, query, cancellationToken);
    }
}
