using SftpSync.Business.Interface;
using SftpSync.Domain.Model;
using SftpSync.Web.DTO;
using SftpSync.Web.Hubs;

namespace SftpSync.Web;

public sealed class SyncDashboardNotifier : ISyncDashboardNotifier
{
    private readonly IDashboardBroadcaster _broadcaster;

    public SyncDashboardNotifier(IDashboardBroadcaster broadcaster)
    {
        _broadcaster = broadcaster;
    }

    public Task NotifyRunUpdatedAsync(SftpSyncRun run, CancellationToken cancellationToken = default)
    {
        return _broadcaster.RunUpdatedAsync(ToRunResponse(run), cancellationToken);
    }

    public Task NotifyQueueItemUpdatedAsync(
        DownloadQueueItem item,
        CancellationToken cancellationToken = default)
    {
        return _broadcaster.QueueItemUpdatedAsync(ToQueueItemResponse(item), cancellationToken);
    }

    internal static SftpSyncRunResponse ToRunResponse(SftpSyncRun run)
    {
        return new SftpSyncRunResponse(
            run.Id,
            run.JobId,
            run.JobName,
            run.Status,
            run.StartedAt,
            run.CompletedAt,
            run.TotalFileCount,
            run.CompletedFileCount,
            run.SkippedFileCount,
            run.FailedFileCount,
            run.TotalBytes,
            run.DownloadedBytes,
            run.CurrentBytesPerSecond,
            run.ErrorMessage);
    }

    internal static DownloadQueueItemResponse ToQueueItemResponse(DownloadQueueItem item)
    {
        return new DownloadQueueItemResponse(
            item.Id,
            item.JobId,
            item.SyncRunId,
            item.RemotePath,
            item.DestinationPath,
            item.FileSizeBytes,
            item.RemoteModifiedAt,
            item.Status,
            item.BytesDownloaded,
            item.CurrentBytesPerSecond,
            item.RetryCount,
            item.HandledReason,
            item.ErrorMessage,
            item.QueuedAt,
            item.StartedAt,
            item.CompletedAt,
            item.UpdatedAt,
            item.IsGroup,
            item.GroupRemotePath,
            item.ChildCount);
    }
}
