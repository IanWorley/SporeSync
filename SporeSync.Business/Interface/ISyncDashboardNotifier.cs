using SporeSync.Domain.Model;

namespace SporeSync.Business.Interface;

public interface ISyncDashboardNotifier
{
    Task NotifyRunUpdatedAsync(SporeSyncRun run, CancellationToken cancellationToken = default);

    Task NotifyQueueItemUpdatedAsync(
        DownloadQueueItem item,
        CancellationToken cancellationToken = default);
}
