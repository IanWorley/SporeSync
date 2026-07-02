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

    public async Task<RetryQueueItemResult> RetryAsync(
        Guid runId,
        Guid queueItemId,
        CancellationToken cancellationToken = default)
    {
        var item = await _repository.GetByIdAsync(queueItemId, cancellationToken);
        if (item is null || item.SyncRunId != runId)
        {
            return new RetryQueueItemResult(RetryQueueItemStatus.NotFound, null);
        }

        var retried = await _repository.RetryAsync(queueItemId, cancellationToken);
        if (retried is null)
        {
            return new RetryQueueItemResult(RetryQueueItemStatus.NotRetryable, item);
        }

        return new RetryQueueItemResult(RetryQueueItemStatus.Retried, retried);
    }
}
