using SporeSync.Domain.Interfaces;
using SporeSync.Domain.Models;
using Directory = System.IO.Directory;

namespace SporeSync.Application.Services;

public class QueueItemService : IQueueService
{
    private readonly Queue<TrackedItem> _queue;

    public QueueItemService()
    {
        _queue = new Queue<TrackedItem>();
    }

    public Task EnqueueSyncAsync(TrackedItem item)
    {
        _queue.Enqueue(item);
        return Task.CompletedTask;
    }

    /// <summary>
    /// Checks an IEnumerable of TrackedItems and adds only those that haven't been added to the queue yet.
    /// Uses RemotePath as the unique identifier to determine if an item already exists.
    /// </summary>
    /// <param name="items">The IEnumerable of TrackedItems to check</param>
    /// <returns>Task containing the count of items that were added</returns>
    public Task<int> AddUniqueItemsAsync(IEnumerable<TrackedItem> items)
    {
        if (items == null)
            return Task.FromResult(0);

        var addedCount = 0;

        var existingRemotePaths = _queue.Select(item => item.RemotePath).ToHashSet();

        foreach (var item in items)
        {
            // Skip items with null or empty RemotePath
            if (string.IsNullOrEmpty(item.RemotePath))
                continue;



            // Check if this item's RemotePath is not already in the queue
            if (!existingRemotePaths.Contains(item.RemotePath))
            {
                _queue.Enqueue(item);
                existingRemotePaths.Add(item.RemotePath); // Add to set to prevent duplicates in same batch
                addedCount++;
            }
        }

        return Task.FromResult(addedCount);
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
        return Task.FromResult(_queue.FirstOrDefault(item => item.RemotePath == itemId));
    }

    public Task<bool> UpdateSyncQueueItemAsync(TrackedItem item)
    {
        var itemToUpdate = _queue.FirstOrDefault(x => x.RemotePath == item.RemotePath);

        if (itemToUpdate != null)
        {
            itemToUpdate.Status = item.Status;
            itemToUpdate.DirectoryId = item.DirectoryId;
            itemToUpdate.FileName = item.FileName ?? itemToUpdate.FileName;
            itemToUpdate.DestinationFilePath = item.DestinationFilePath ?? itemToUpdate.DestinationFilePath;
            itemToUpdate.FileSize = item.FileSize;
            itemToUpdate.FileExtension = item.FileExtension ?? itemToUpdate.FileExtension;
            itemToUpdate.FileHash = item.FileHash ?? itemToUpdate.FileHash;

            return Task.FromResult(true);
        }

        return Task.FromResult(false);
    }

    public Task<IEnumerable<TrackedItem>> GetPendingItemsAsync()
    {
        return Task.FromResult(_queue.Where(item => item.Status == FileStatus.Tracked));
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
