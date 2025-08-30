using SporeSync.Domain.Models;

namespace SporeSync.Domain.Interfaces
{
    public interface IQueueService
    {
        Task EnqueueSyncAsync(TrackedItem item);
        Task<TrackedItem?> DequeueSyncAsync(CancellationToken cancellationToken = default);
        Task<IEnumerable<TrackedItem>> GetPendingItemsAsync();
        Task<bool> RemoveItemAsync(Guid itemId);
        Task<int> GetQueueCountAsync();
        Task<TrackedItem?> GetSyncQueueItemAsync(Guid itemId);
        Task<bool> UpdateSyncQueueItemAsync(TrackedItem item);

        /// <summary>
        /// Checks an IEnumerable of TrackedItems and adds only those that haven't been added to the queue yet.
        /// Uses RemotePath as the unique identifier to determine if an item already exists.
        /// </summary>
        /// <param name="items">The IEnumerable of TrackedItems to check</param>
        /// <returns>Task containing the count of items that were added</returns>
        Task<int> AddUniqueItemsAsync(IEnumerable<TrackedItem> items);

        /// <summary>
        /// Alternative method that uses a custom predicate to determine uniqueness
        /// </summary>
        /// <param name="items">The IEnumerable of TrackedItems to check</param>
        /// <param name="isDuplicate">Predicate to determine if an item is a duplicate</param>
        /// <returns>Task containing the count of items that were added</returns>
        Task<int> AddUniqueItemsAsync(IEnumerable<TrackedItem> items, Func<TrackedItem, TrackedItem, bool> isDuplicate);
    }

    public enum SyncOperation
    {
        Create,
        Update,
        Delete,
        Rename
    }

    public enum SyncStatus
    {
        Pending,
        InProgress,
        Completed,
        Failed,
    }
}
