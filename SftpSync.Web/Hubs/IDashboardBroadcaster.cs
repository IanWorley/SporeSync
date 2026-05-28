using SftpSync.Web.DTO;

namespace SftpSync.Web.Hubs;

public interface IDashboardBroadcaster
{
    Task RunUpdatedAsync(SftpSyncRunResponse run, CancellationToken cancellationToken = default);

    Task QueueItemUpdatedAsync(DownloadQueueItemResponse item, CancellationToken cancellationToken = default);

    Task QueueItemRemovedAsync(Guid runId, Guid queueItemId, CancellationToken cancellationToken = default);

    Task LogAppendedAsync(object logEntry, CancellationToken cancellationToken = default);
}
