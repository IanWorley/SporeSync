using System.Diagnostics;
using Microsoft.Extensions.Logging;
using SporeSync.Business.Interface;
using SporeSync.Business.Observability;
using SporeSync.Business.Sftp;
using SporeSync.Domain.Interface;
using SporeSync.Domain.Model;

namespace SporeSync.Business.Worker;

public interface ISyncRunOrchestrator
{
    Task<SporeSyncRun> ScanAsync(
        SporeSyncJob job,
        SporeSyncRun run,
        CancellationToken cancellationToken = default);
}

public sealed class SyncRunOrchestrator : ISyncRunOrchestrator
{
    private readonly ISporeSyncRunRepository _runRepository;
    private readonly IDownloadQueueItemRepository _queueRepository;
    private readonly RealSftpDirectoryScanner _scanner;
    private readonly IChangeDetector _changeDetector;
    private readonly ISyncDashboardNotifier _notifier;
    private readonly SporeSyncMetrics _metrics;
    private readonly ILogger<SyncRunOrchestrator> _logger;

    public SyncRunOrchestrator(
        ISporeSyncRunRepository runRepository,
        IDownloadQueueItemRepository queueRepository,
        RealSftpDirectoryScanner scanner,
        IChangeDetector changeDetector,
        ISyncDashboardNotifier notifier,
        SporeSyncMetrics metrics,
        ILogger<SyncRunOrchestrator> logger)
    {
        _runRepository = runRepository;
        _queueRepository = queueRepository;
        _scanner = scanner;
        _changeDetector = changeDetector;
        _notifier = notifier;
        _metrics = metrics;
        _logger = logger;
    }

    public async Task<SporeSyncRun> ScanAsync(
        SporeSyncJob job,
        SporeSyncRun run,
        CancellationToken cancellationToken = default)
    {
        run = await UpdateRunAsync(run.Id, new UpdateSporeSyncRunStatus
        {
            Id = run.Id,
            Status = "scanning"
        }, cancellationToken);

        var stopwatch = Stopwatch.StartNew();
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

            // Note: failed items are intentionally NOT auto-requeued here. Transient failures are
            // retried by the download worker with an exponential backoff budget; once that budget
            // is exhausted the item is dead-lettered as terminal 'failed' and only revived by a
            // remote content change (ChangeDetector) or an explicit manual retry via the API.

            stopwatch.Stop();
            _metrics.RecordScanCompleted(stopwatch.Elapsed.TotalSeconds, changes.EnqueuedVisibleCount);
            _logger.LogInformation(
                "Scan completed for job {JobId} run {RunId}: {EnqueuedCount} entries enqueued ({EnqueuedBytes} bytes), {RemoteDeletedCount} remote-deleted, in {DurationMs} ms",
                job.Id,
                run.Id,
                changes.EnqueuedVisibleCount,
                changes.EnqueuedTotalBytes,
                remoteDeletedItems.Count,
                stopwatch.ElapsedMilliseconds);

            var totalVisibleCount = changes.EnqueuedVisibleCount + remoteDeletedItems.Count;
            if (changes.EnqueuedVisibleCount == 0)
            {
                return await UpdateRunAsync(run.Id, new UpdateSporeSyncRunStatus
                {
                    Id = run.Id,
                    Status = "completed",
                    TotalFileCount = totalVisibleCount,
                    TotalBytes = 0,
                    SkippedFileCount = remoteDeletedItems.Count
                }, cancellationToken);
            }

            return await UpdateRunAsync(run.Id, new UpdateSporeSyncRunStatus
            {
                Id = run.Id,
                Status = "downloading",
                TotalFileCount = totalVisibleCount,
                TotalBytes = changes.EnqueuedTotalBytes
            }, cancellationToken);
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            _metrics.RecordScanFailed(stopwatch.Elapsed.TotalSeconds);
            _logger.LogError(ex, "Scan failed for job {JobId} run {RunId}", job.Id, run.Id);
            return await UpdateRunAsync(run.Id, new UpdateSporeSyncRunStatus
            {
                Id = run.Id,
                Status = "failed",
                ErrorMessage = ex.Message
            }, cancellationToken);
        }
    }

    private async Task<SporeSyncRun> UpdateRunAsync(
        Guid runId,
        UpdateSporeSyncRunStatus update,
        CancellationToken cancellationToken)
    {
        var run = await _runRepository.UpdateStatusAsync(update, cancellationToken);
        await _notifier.NotifyRunUpdatedAsync(run, cancellationToken);
        return run;
    }
}
