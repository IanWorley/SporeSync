using SporeSync.Web.DTO;

namespace SporeSync.Web.Hubs;

public interface IDashboardBroadcaster
{
    Task RunUpdatedAsync(SporeSyncRunResponse run, CancellationToken cancellationToken = default);

    // Supports opaque groups (Phase 5 per plan:364): the item may have IsGroup=true with subtree
    // aggregates in the byte/progress fields. Broadcasting the visible group row is sufficient
    // (clients treat all rows as opaque units; see DashboardBroadcaster.cs).
    Task QueueItemUpdatedAsync(DownloadQueueItemResponse item, CancellationToken cancellationToken = default);

    Task QueueItemRemovedAsync(Guid runId, Guid queueItemId, CancellationToken cancellationToken = default);

    Task LogAppendedAsync(object logEntry, CancellationToken cancellationToken = default);
}
