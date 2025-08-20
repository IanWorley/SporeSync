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
