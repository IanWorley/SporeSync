using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
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
    private readonly SporeSyncOptions _options;
    private readonly ILogger<SyncRunOrchestrator> _logger;

    public SyncRunOrchestrator(
        ISporeSyncRunRepository runRepository,
        IDownloadQueueItemRepository queueRepository,
        RealSftpDirectoryScanner scanner,
        IChangeDetector changeDetector,
        ISyncDashboardNotifier notifier,
        SporeSyncMetrics metrics,
        IOptions<SporeSyncOptions> options,
        ILogger<SyncRunOrchestrator> logger)
    {
        _runRepository = runRepository;
        _queueRepository = queueRepository;
        _scanner = scanner;
        _changeDetector = changeDetector;
        _notifier = notifier;
        _metrics = metrics;
        _options = options.Value;
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
            Status = "scanning",
            LeaseSeconds = _options.RunScanLeaseSeconds
        }, cancellationToken);

        var stopwatch = Stopwatch.StartNew();
        using var leaseRenewalCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var leaseRenewalTask = RenewLeaseWhileScanningAsync(run.Id, leaseRenewalCts.Token);
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

            // Single transaction so groups and their leaves become visible atomically;
            // nothing is claimable until the run transitions to 'downloading' below.
            var enqueuedItems = await _queueRepository.UpsertManyAsync(
                changes.EntriesToEnqueue
                    .Select(entry => new UpsertDownloadQueueItem
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
                    })
                    .ToArray(),
                cancellationToken);

            foreach (var item in enqueuedItems)
            {
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
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Graceful shutdown/cancellation: record the run as cancelled (not failed)
            // using a fresh token so the status write itself is not cancelled.
            _logger.LogInformation(
                "Scan cancelled for job {JobId} run {RunId}; marking the run as cancelled.",
                job.Id,
                run.Id);
            await UpdateRunAsync(run.Id, new UpdateSporeSyncRunStatus
            {
                Id = run.Id,
                Status = "cancelled"
            }, CancellationToken.None);
            throw;
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
        finally
        {
            leaseRenewalCts.Cancel();
            await leaseRenewalTask;
        }
    }

    private async Task RenewLeaseWhileScanningAsync(
        Guid runId,
        CancellationToken cancellationToken)
    {
        var interval = TimeSpan.FromSeconds(Math.Max(1, _options.RunScanLeaseSeconds / 3.0));

        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(interval, cancellationToken);
                if (!await _runRepository.RenewLeaseAsync(runId, _options.RunScanLeaseSeconds, cancellationToken))
                {
                    _logger.LogWarning(
                        "Sync run {RunId} is no longer lease-renewable; stopping scan lease renewal.",
                        runId);
                    return;
                }
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to renew scanning lease for sync run {RunId}.", runId);
            }
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
