using Microsoft.Extensions.Logging;
using SftpSync.Business.Interface;
using SftpSync.Business.Sftp;
using SftpSync.Domain.Interface;
using SftpSync.Domain.Model;

namespace SftpSync.Business.Worker;

public interface ISyncRunOrchestrator
{
    Task<SftpSyncRun> ScanAsync(
        SftpSyncJob job,
        SftpSyncRun run,
        CancellationToken cancellationToken = default);
}

public sealed class SyncRunOrchestrator : ISyncRunOrchestrator
{
    private readonly ISftpSyncRunRepository _runRepository;
    private readonly IDownloadQueueItemRepository _queueRepository;
    private readonly RealSftpDirectoryScanner _scanner;
    private readonly IChangeDetector _changeDetector;
    private readonly ISyncDashboardNotifier _notifier;
    private readonly ILogger<SyncRunOrchestrator> _logger;

    public SyncRunOrchestrator(
        ISftpSyncRunRepository runRepository,
        IDownloadQueueItemRepository queueRepository,
        RealSftpDirectoryScanner scanner,
        IChangeDetector changeDetector,
        ISyncDashboardNotifier notifier,
        ILogger<SyncRunOrchestrator> logger)
    {
        _runRepository = runRepository;
        _queueRepository = queueRepository;
        _scanner = scanner;
        _changeDetector = changeDetector;
        _notifier = notifier;
        _logger = logger;
    }

    public async Task<SftpSyncRun> ScanAsync(
        SftpSyncJob job,
        SftpSyncRun run,
        CancellationToken cancellationToken = default)
    {
        run = await UpdateRunAsync(run.Id, new UpdateSftpSyncRunStatus
        {
            Id = run.Id,
            Status = "scanning"
        }, cancellationToken);

        try
        {
            var scanResult = await _scanner.ScanFirstLevelAsync(
                job.ConnectionProfileId,
                job.SourcePath,
                job.DestinationPath,
                cancellationToken);

            var syncedState = await _queueRepository.GetSyncedStateAsync(job.Id, cancellationToken);
            var changes = _changeDetector.DetectChanges(scanResult, syncedState);
            var remoteDeletedItems = await _queueRepository.MarkRemoteDeletedAsync(
                job.Id,
                run.Id,
                changes.RemoteDeletedPaths,
                cancellationToken);

            foreach (var item in remoteDeletedItems)
            {
                await _notifier.NotifyQueueItemUpdatedAsync(item, cancellationToken);
            }

            foreach (var entry in changes.EntriesToEnqueue)
            {
                var item = await _queueRepository.UpsertAsync(new UpsertDownloadQueueItem
                {
                    JobId = job.Id,
                    SyncRunId = run.Id,
                    RemotePath = entry.RemotePath,
                    DestinationPath = entry.DestinationPath,
                    FileSizeBytes = entry.FileSizeBytes,
                    RemoteModifiedAt = entry.RemoteModifiedAt,
                    IsGroup = entry.IsGroup,
                    GroupRemotePath = entry.GroupRemotePath,
                    ChildCount = entry.ChildCount
                }, cancellationToken);

                await _notifier.NotifyQueueItemUpdatedAsync(item, cancellationToken);
            }

            await _queueRepository.RequeueFailedAsync(job.Id, run.Id, cancellationToken);

            var totalVisibleCount = changes.EnqueuedVisibleCount + remoteDeletedItems.Count;
            if (changes.EnqueuedVisibleCount == 0)
            {
                return await UpdateRunAsync(run.Id, new UpdateSftpSyncRunStatus
                {
                    Id = run.Id,
                    Status = "completed",
                    TotalFileCount = totalVisibleCount,
                    TotalBytes = 0,
                    SkippedFileCount = remoteDeletedItems.Count
                }, cancellationToken);
            }

            return await UpdateRunAsync(run.Id, new UpdateSftpSyncRunStatus
            {
                Id = run.Id,
                Status = "downloading",
                TotalFileCount = totalVisibleCount,
                TotalBytes = changes.EnqueuedTotalBytes
            }, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Scan failed for job {JobId} run {RunId}", job.Id, run.Id);
            return await UpdateRunAsync(run.Id, new UpdateSftpSyncRunStatus
            {
                Id = run.Id,
                Status = "failed",
                ErrorMessage = ex.Message
            }, cancellationToken);
        }
    }

    private async Task<SftpSyncRun> UpdateRunAsync(
        Guid runId,
        UpdateSftpSyncRunStatus update,
        CancellationToken cancellationToken)
    {
        var run = await _runRepository.UpdateStatusAsync(update, cancellationToken);
        await _notifier.NotifyRunUpdatedAsync(run, cancellationToken);
        return run;
    }
}
