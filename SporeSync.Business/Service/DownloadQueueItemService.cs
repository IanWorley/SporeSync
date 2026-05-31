using SporeSync.Business.Interface;
using SporeSync.Domain.Interface;
using SporeSync.Domain.Model;

namespace SporeSync.Business.Service;

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

    public Task<IReadOnlyList<DownloadQueueItem>> GetLeavesForGroupAsync(
        Guid runId,
        string groupRemotePath,
        CancellationToken cancellationToken = default)
    {
        return _repository.GetLeavesForGroupAsync(runId, groupRemotePath, cancellationToken);
    }
}
