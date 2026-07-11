using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SporeSync.Business.Interface;
using SporeSync.Business.Observability;
using SporeSync.Business.Sftp;
using SporeSync.Domain.Interface;
using SporeSync.Domain.Model;

namespace SporeSync.Business.Worker;

public sealed class DownloadWorkerHostedService : BackgroundService
{
    internal const string AwaitingRemoteStabilityReason = "awaiting_remote_stability";

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly SporeSyncOptions _options;
    private readonly SporeSyncMetrics _metrics;
    private readonly DownloadRetryPolicy _retryPolicy;
    private readonly ILogger<DownloadWorkerHostedService> _logger;

    public DownloadWorkerHostedService(
        IServiceScopeFactory scopeFactory,
        IOptions<SporeSyncOptions> options,
        SporeSyncMetrics metrics,
        DownloadRetryPolicy retryPolicy,
        ILogger<DownloadWorkerHostedService> logger)
    {
        _scopeFactory = scopeFactory;
        _options = options.Value;
        _metrics = metrics;
        _retryPolicy = retryPolicy;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var pollInterval = TimeSpan.FromMilliseconds(_options.DownloadPollIntervalMs);

        while (!stoppingToken.IsCancellationRequested)
        {
            var processed = false;
            try
            {
                processed = await ProcessNextItemAsync(stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "Download worker iteration failed.");
            }

            if (!processed)
            {
                try
                {
                    await Task.Delay(pollInterval, stoppingToken);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }
    }

    internal async Task<bool> ProcessNextItemAsync(CancellationToken cancellationToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var queueRepository = scope.ServiceProvider.GetRequiredService<IDownloadQueueItemRepository>();
        var runRepository = scope.ServiceProvider.GetRequiredService<ISporeSyncRunRepository>();
        var jobRepository = scope.ServiceProvider.GetRequiredService<ISporeSyncJobRepository>();
        var downloader = scope.ServiceProvider.GetRequiredService<ISftpFileDownloader>();
        var clientFactory = scope.ServiceProvider.GetRequiredService<ISftpClientFactory>();
        var notifier = scope.ServiceProvider.GetRequiredService<ISyncDashboardNotifier>();

        var item = await queueRepository.ClaimNextAsync(_options.DownloadLeaseSeconds, cancellationToken);
        if (item is null || item.SyncRunId is null)
        {
            return false;
        }

        DownloadQueueItem updatedItem;
        using var renewalCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var leaseRenewalTask = RenewLeaseWhileProcessingAsync(queueRepository, item.Id, renewalCts.Token);
        try
        {
            var job = await jobRepository.GetByIdAsync(item.JobId, cancellationToken)
                ?? throw new InvalidOperationException($"Job '{item.JobId}' was not found for queue item '{item.Id}'.");

            if (item.IsGroup)
            {
                updatedItem = await ProcessGroupAsync(
                    item,
                    job,
                    queueRepository,
                    runRepository,
                    downloader,
                    clientFactory,
                    notifier,
                    cancellationToken);
            }
            else
            {
                updatedItem = await ProcessSingleFileAsync(
                    item,
                    job,
                    queueRepository,
                    downloader,
                    notifier,
                    cancellationToken);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Graceful shutdown: return the claim to the queue instead of recording
            // a bogus failure. Completed group leaves keep their progress.
            await ReleaseClaimAsync(queueRepository, notifier, item.Id);
            throw;
        }
        finally
        {
            renewalCts.Cancel();
            await leaseRenewalTask;
        }

        await notifier.NotifyQueueItemUpdatedAsync(updatedItem, cancellationToken);

        var run = await runRepository.RecalculateAggregatesAsync(item.SyncRunId.Value, cancellationToken);
        await notifier.NotifyRunUpdatedAsync(run, cancellationToken);

        // A run cancelled while this item was downloading stays cancelled.
        if (string.Equals(run.Status, "cancelled", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (!await runRepository.HasPendingDownloadsAsync(item.SyncRunId.Value, cancellationToken))
        {
            run = await runRepository.UpdateStatusAsync(new UpdateSporeSyncRunStatus
            {
                Id = item.SyncRunId.Value,
                Status = "completed"
            }, cancellationToken);
            await notifier.NotifyRunUpdatedAsync(run, cancellationToken);
        }

        return true;
    }

    private async Task RenewLeaseWhileProcessingAsync(
        IDownloadQueueItemRepository queueRepository,
        Guid itemId,
        CancellationToken cancellationToken)
    {
        var interval = TimeSpan.FromSeconds(Math.Max(1, _options.DownloadLeaseSeconds / 3.0));

        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(interval, cancellationToken);
                if (!await queueRepository.RenewLeaseAsync(itemId, _options.DownloadLeaseSeconds, cancellationToken))
                {
                    _logger.LogWarning(
                        "Queue item {ItemId} is no longer claimed; stopping lease renewal.",
                        itemId);
                    return;
                }
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to renew lease for queue item {ItemId}.", itemId);
            }
        }
    }

    private async Task ReleaseClaimAsync(
        IDownloadQueueItemRepository queueRepository,
        ISyncDashboardNotifier notifier,
        Guid itemId)
    {
        try
        {
            var released = await queueRepository.ReleaseAsync(itemId, CancellationToken.None);
            if (released is not null)
            {
                await notifier.NotifyQueueItemUpdatedAsync(released, CancellationToken.None);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "Failed to release claimed queue item {ItemId} during shutdown; the recovery sweep will requeue it.",
                itemId);
        }
    }

    private async Task<DownloadQueueItem> ProcessSingleFileAsync(
        DownloadQueueItem item,
        SporeSyncJob job,
        IDownloadQueueItemRepository queueRepository,
        ISftpFileDownloader downloader,
        ISyncDashboardNotifier notifier,
        CancellationToken cancellationToken)
    {
        var progress = new FireAndForgetDownloadProgress(async (bytes, token) =>
        {
            var partial = await queueRepository.UpdateProgressAsync(new UpdateDownloadQueueItemProgress
            {
                Id = item.Id,
                Status = "downloading",
                BytesDownloaded = bytes,
                CurrentBytesPerSecond = null,
                ErrorMessage = null
            }, token);
            await notifier.NotifyQueueItemUpdatedAsync(partial, token);
        }, cancellationToken);

        SftpDownloadResult result;
        try
        {
            result = await downloader.DownloadAsync(
                job.ConnectionProfileId,
                item.RemotePath,
                item.DestinationPath,
                progress,
                cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(
                ex,
                "Download setup failed for {RemotePath} in job {JobId}",
                item.RemotePath,
                job.Id);
            result = SftpDownloadResult.Failure(ex.Message);
        }

        RecordDownloadResult(job.Id, item.RemotePath, result);

        if (result.Success)
        {
            return await progress.CompleteAsync(token => queueRepository.UpdateProgressAsync(new UpdateDownloadQueueItemProgress
            {
                Id = item.Id,
                Status = "completed",
                BytesDownloaded = result.BytesDownloaded,
                CurrentBytesPerSecond = result.BytesPerSecond
            }, token));
        }

        if (result.Deferred)
        {
            // The remote file is still inside the stability window; check again later
            // without consuming retry budget.
            return await progress.CompleteAsync(token => queueRepository.DeferAsync(
                item.Id,
                DateTimeOffset.UtcNow + _retryPolicy.StabilityRecheckDelay,
                AwaitingRemoteStabilityReason,
                bytesDownloaded: null,
                token));
        }

        return await progress.CompleteAsync(token => RecordFailureAsync(
            queueRepository,
            item,
            result.ErrorMessage,
            bytesDownloaded: null,
            token));
    }

    private async Task<DownloadQueueItem> ProcessGroupAsync(
        DownloadQueueItem groupItem,
        SporeSyncJob job,
        IDownloadQueueItemRepository queueRepository,
        ISporeSyncRunRepository runRepository,
        ISftpFileDownloader downloader,
        ISftpClientFactory clientFactory,
        ISyncDashboardNotifier notifier,
        CancellationToken cancellationToken)
    {
        var leaves = await queueRepository.GetLeavesForGroupAsync(
            groupItem.SyncRunId!.Value,
            groupItem.RemotePath,
            cancellationToken);

        long groupBytesDownloaded = 0;
        decimal? latestRate = null;
        var groupFailed = false;
        var groupDeferred = false;

        IConnectedSftpClient? connection = null;
        try
        {
            foreach (var leaf in leaves)
            {
                if (await IsRunCancelledAsync(groupItem.SyncRunId.Value, runRepository, cancellationToken))
                {
                    return await MarkGroupCancelledAsync(groupItem, queueRepository, groupBytesDownloaded, cancellationToken);
                }

                var currentLeaf = await queueRepository.GetByIdAsync(leaf.Id, cancellationToken);
                if (currentLeaf is null)
                {
                    continue;
                }

                if (string.Equals(currentLeaf.Status, "completed", StringComparison.OrdinalIgnoreCase))
                {
                    groupBytesDownloaded += currentLeaf.BytesDownloaded;
                    continue;
                }

                // Leaves marked skipped (e.g. deleted remotely) must not be downloaded
                // and must not fail the group.
                if (string.Equals(currentLeaf.Status, "skipped", StringComparison.OrdinalIgnoreCase))
                {
                    if (string.Equals(currentLeaf.HandledReason, "run_cancelled", StringComparison.OrdinalIgnoreCase))
                    {
                        return await MarkGroupCancelledAsync(groupItem, queueRepository, groupBytesDownloaded, cancellationToken);
                    }

                    continue;
                }

                if (!string.Equals(currentLeaf.Status, "queued", StringComparison.OrdinalIgnoreCase)
                    && !string.Equals(currentLeaf.Status, "downloading", StringComparison.OrdinalIgnoreCase))
                {
                    if (!string.Equals(currentLeaf.Status, "failed", StringComparison.OrdinalIgnoreCase))
                    {
                        groupFailed = true;
                        continue;
                    }
                }

                var completedBeforeLeaf = groupBytesDownloaded;
                var leafProgress = new FireAndForgetDownloadProgress(async (bytes, token) =>
                {
                    await queueRepository.UpdateProgressAsync(new UpdateDownloadQueueItemProgress
                    {
                        Id = currentLeaf.Id,
                        Status = "downloading",
                        BytesDownloaded = bytes,
                        CurrentBytesPerSecond = null,
                        ErrorMessage = null
                    }, token);

                    var visibleGroupBytes = Math.Min(
                        groupItem.FileSizeBytes,
                        completedBeforeLeaf + Math.Min(bytes, currentLeaf.FileSizeBytes));
                    var groupPartial = await queueRepository.UpdateProgressAsync(new UpdateDownloadQueueItemProgress
                    {
                        Id = groupItem.Id,
                        Status = "downloading",
                        BytesDownloaded = visibleGroupBytes,
                        CurrentBytesPerSecond = null,
                        ErrorMessage = null
                    }, token);
                    await notifier.NotifyQueueItemUpdatedAsync(groupPartial, token);
                }, cancellationToken);

                SftpDownloadResult leafResult;
                try
                {
                    if (connection is not null && !connection.IsConnected)
                    {
                        await connection.DisposeAsync();
                        connection = null;
                    }

                    connection ??= await clientFactory.ConnectAsync(job.ConnectionProfileId, cancellationToken);
                    leafResult = await downloader.DownloadAsync(
                        connection,
                        currentLeaf.RemotePath,
                        currentLeaf.DestinationPath,
                        leafProgress,
                        cancellationToken);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    _logger.LogWarning(
                        ex,
                        "Download setup failed for grouped leaf {RemotePath} in job {JobId}",
                        currentLeaf.RemotePath,
                        job.Id);
                    leafResult = SftpDownloadResult.Failure(ex.Message);
                }

                RecordDownloadResult(job.Id, currentLeaf.RemotePath, leafResult);

                if (leafResult.Success)
                {
                    await leafProgress.CompleteAsync(token => queueRepository.UpdateProgressAsync(new UpdateDownloadQueueItemProgress
                    {
                        Id = currentLeaf.Id,
                        Status = "completed",
                        BytesDownloaded = leafResult.BytesDownloaded,
                        CurrentBytesPerSecond = leafResult.BytesPerSecond
                    }, token));

                    groupBytesDownloaded += leafResult.BytesDownloaded;
                    latestRate = leafResult.BytesPerSecond;
                    continue;
                }

                if (leafResult.Deferred)
                {
                    await leafProgress.CompleteAsync(token => queueRepository.UpdateProgressAsync(new UpdateDownloadQueueItemProgress
                    {
                        Id = currentLeaf.Id,
                        Status = "queued",
                        BytesDownloaded = currentLeaf.BytesDownloaded,
                        HandledReason = AwaitingRemoteStabilityReason
                    }, token));

                    groupDeferred = true;
                    if (await IsRunCancelledAsync(groupItem.SyncRunId.Value, runRepository, cancellationToken))
                    {
                        return await MarkGroupCancelledAsync(groupItem, queueRepository, groupBytesDownloaded, cancellationToken);
                    }

                    continue;
                }

                await leafProgress.CompleteAsync(token => queueRepository.UpdateProgressAsync(new UpdateDownloadQueueItemProgress
                {
                    Id = currentLeaf.Id,
                    Status = "failed",
                    BytesDownloaded = currentLeaf.BytesDownloaded,
                    ErrorMessage = leafResult.ErrorMessage
                }, token));

                groupFailed = true;
                if (connection is not null)
                {
                    await connection.DisposeAsync();
                    connection = null;
                }

                if (await IsRunCancelledAsync(groupItem.SyncRunId.Value, runRepository, cancellationToken))
                {
                    return await MarkGroupCancelledAsync(groupItem, queueRepository, groupBytesDownloaded, cancellationToken);
                }
            }
        }
        finally
        {
            if (connection is not null)
            {
                await connection.DisposeAsync();
            }
        }

        if (groupFailed)
        {
            // The group carries the retry budget for its subtree: failed leaves are retried on the
            // group's next claim (only non-completed leaves are re-attempted) until the budget is
            // exhausted, at which point the group is dead-lettered as terminal 'failed'.
            return await RecordFailureAsync(
                queueRepository,
                groupItem,
                "One or more files in the group failed to download.",
                groupBytesDownloaded,
                cancellationToken);
        }

        if (groupDeferred)
        {
            return await queueRepository.DeferAsync(
                groupItem.Id,
                DateTimeOffset.UtcNow + _retryPolicy.StabilityRecheckDelay,
                AwaitingRemoteStabilityReason,
                groupBytesDownloaded,
                cancellationToken);
        }

        if (await IsRunCancelledAsync(groupItem.SyncRunId.Value, runRepository, cancellationToken))
        {
            return await MarkGroupCancelledAsync(groupItem, queueRepository, groupBytesDownloaded, cancellationToken);
        }

        return await queueRepository.UpdateProgressAsync(new UpdateDownloadQueueItemProgress
        {
            Id = groupItem.Id,
            Status = "completed",
            BytesDownloaded = groupBytesDownloaded,
            CurrentBytesPerSecond = latestRate
        }, cancellationToken);
    }

    private static async Task<bool> IsRunCancelledAsync(
        Guid runId,
        ISporeSyncRunRepository runRepository,
        CancellationToken cancellationToken)
    {
        var currentRun = await runRepository.GetByIdAsync(runId, cancellationToken);
        return string.Equals(currentRun?.Status, "cancelled", StringComparison.OrdinalIgnoreCase);
    }

    private static Task<DownloadQueueItem> MarkGroupCancelledAsync(
        DownloadQueueItem groupItem,
        IDownloadQueueItemRepository queueRepository,
        long groupBytesDownloaded,
        CancellationToken cancellationToken)
    {
        return queueRepository.UpdateProgressAsync(new UpdateDownloadQueueItemProgress
        {
            Id = groupItem.Id,
            Status = "skipped",
            BytesDownloaded = groupBytesDownloaded,
            CurrentBytesPerSecond = null,
            ErrorMessage = null,
            HandledReason = "run_cancelled"
        }, cancellationToken);
    }

    private void RecordDownloadResult(Guid jobId, string remotePath, SftpDownloadResult result)
    {
        if (result.Success)
        {
            _metrics.RecordDownloadCompleted(result.BytesDownloaded);
            _logger.LogInformation(
                "Downloaded {RemotePath} for job {JobId}: {BytesDownloaded} bytes at {BytesPerSecond} B/s",
                remotePath,
                jobId,
                result.BytesDownloaded,
                result.BytesPerSecond);
        }
        else if (result.Deferred)
        {
            _logger.LogInformation(
                "Download deferred for {RemotePath} in job {JobId}: {Reason}",
                remotePath,
                jobId,
                result.ErrorMessage);
        }
        else
        {
            _metrics.RecordDownloadFailed();
            _logger.LogWarning(
                "Download failed for {RemotePath} in job {JobId}: {ErrorMessage}",
                remotePath,
                jobId,
                result.ErrorMessage);
        }
    }

    private sealed class FireAndForgetDownloadProgress : IProgress<long>
    {
        private readonly Func<long, CancellationToken, Task> _reportAsync;
        private readonly CancellationToken _cancellationToken;
        private readonly SemaphoreSlim _gate = new(1, 1);
        private int _terminalUpdateStarted;

        public FireAndForgetDownloadProgress(
            Func<long, CancellationToken, Task> reportAsync,
            CancellationToken cancellationToken)
        {
            _reportAsync = reportAsync;
            _cancellationToken = cancellationToken;
        }

        public void Report(long bytesDownloaded)
        {
            if (Volatile.Read(ref _terminalUpdateStarted) != 0)
            {
                return;
            }

            _ = ReportAsync(bytesDownloaded);
        }

        public async Task<T> CompleteAsync<T>(Func<CancellationToken, Task<T>> terminalUpdateAsync)
        {
            Interlocked.Exchange(ref _terminalUpdateStarted, 1);
            await _gate.WaitAsync(_cancellationToken);
            try
            {
                return await terminalUpdateAsync(_cancellationToken);
            }
            finally
            {
                _gate.Release();
            }
        }

        private async Task ReportAsync(long bytesDownloaded)
        {
            try
            {
                await _gate.WaitAsync(_cancellationToken);
                try
                {
                    if (Volatile.Read(ref _terminalUpdateStarted) != 0)
                    {
                        return;
                    }

                    await _reportAsync(bytesDownloaded, _cancellationToken);
                }
                finally
                {
                    _gate.Release();
                }
            }
            catch (OperationCanceledException)
            {
            }
            catch
            {
                // Best-effort progress reporting must not fail or strand the download.
            }
        }
    }

    private async Task<DownloadQueueItem> RecordFailureAsync(
        IDownloadQueueItemRepository queueRepository,
        DownloadQueueItem item,
        string? errorMessage,
        long? bytesDownloaded,
        CancellationToken cancellationToken)
    {
        var nextAttemptAt = DateTimeOffset.UtcNow + _retryPolicy.GetRetryDelay(item.RetryCount);
        var updated = await queueRepository.RecordFailureAsync(
            item.Id,
            errorMessage,
            _retryPolicy.MaxRetries,
            nextAttemptAt,
            bytesDownloaded,
            cancellationToken);

        if (string.Equals(updated.Status, "failed", StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogWarning(
                "Queue item {QueueItemId} ({RemotePath}) dead-lettered after {RetryCount} failed attempts: {Error}",
                updated.Id,
                updated.RemotePath,
                updated.RetryCount,
                errorMessage);
        }
        else
        {
            _logger.LogInformation(
                "Queue item {QueueItemId} ({RemotePath}) failed attempt {RetryCount}; retry scheduled for {NextAttemptAt}: {Error}",
                updated.Id,
                updated.RemotePath,
                updated.RetryCount,
                nextAttemptAt,
                errorMessage);
        }

        return updated;
    }
}
