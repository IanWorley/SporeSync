using SftpSync.Domain.Model;

namespace SftpSync.Business.Interface;

public interface ISyncDashboardNotifier
{
    Task NotifyRunUpdatedAsync(SftpSyncRun run, CancellationToken cancellationToken = default);

    Task NotifyQueueItemUpdatedAsync(
        DownloadQueueItem item,
        CancellationToken cancellationToken = default);
}
