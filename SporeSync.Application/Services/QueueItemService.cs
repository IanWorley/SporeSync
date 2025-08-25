using System;
using SporeSync.Domain.Interfaces;
using SporeSync.Domain.Models;

namespace SporeSync.Application.Services;

public class QueueItemService : IQueueService
{
    private readonly Queue<TrackedItem> _queue;

    public Task EnqueueSyncAsync(TrackedItem item)
    {
        _queue.Enqueue(item);
        return Task.CompletedTask;
    }

    public Task<TrackedItem?> DequeueSyncAsync(CancellationToken cancellationToken = default)
    {
        if (_queue.Count > 0)
            return Task.FromResult(_queue.Dequeue());

        return Task.FromResult<TrackedItem?>(null);
    }

    public Task<bool> RemoveItemAsync(string itemId)
    {
        if (_queue.TryDequeue(out var item))
            return Task.FromResult(true);

        return Task.FromResult(false);
    }

    public Task<int> GetQueueCountAsync()
    {
        return Task.FromResult(_queue.Count);
    }

    public Task<TrackedItem?> GetSyncQueueItemAsync(string itemId)
    {
        return Task.FromResult(_queue.FirstOrDefault(item => item.Id == itemId));
    }

    public Task<bool> UpdateSyncQueueItemAsync(TrackedItem item)
    {
        var itemToUpdate = _queue.FirstOrDefault(x => x.Id == item.Id);

        if (itemToUpdate != null)
        {
            itemToUpdate.Status = item.Status;
            itemToUpdate.DirectoryId = item.DirectoryId;
            itemToUpdate.FileName = item.FileName ?? itemToUpdate.FileName;
            itemToUpdate.FilePath = item.FilePath ?? itemToUpdate.FilePath;
            itemToUpdate.FileSize = item.FileSize;
            itemToUpdate.FileExtension = item.FileExtension ?? itemToUpdate.FileExtension;
            itemToUpdate.FileHash = item.FileHash ?? itemToUpdate.FileHash;

            return Task.FromResult(true);
        }

        return Task.FromResult(false);
    }

    public Task<IEnumerable<TrackedItem>> GetPendingItemsAsync()
    {
        throw new NotImplementedException();
    }

    public Task<bool> RemoveItemAsync(Guid itemId)
    {
        throw new NotImplementedException();
    }

    public Task<TrackedItem?> GetSyncQueueItemAsync(Guid itemId)
    {
        throw new NotImplementedException();
    }
}
