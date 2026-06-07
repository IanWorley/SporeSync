using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SporeSync.Business.Interface;
using SporeSync.Business.Sftp;
using SporeSync.Domain.Interface;
using SporeSync.Domain.Model;

namespace SporeSync.Business.Worker;

public sealed class DownloadWorkerHostedService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly SporeSyncOptions _options;
    private readonly ILogger<DownloadWorkerHostedService> _logger;

    public DownloadWorkerHostedService(
        IServiceScopeFactory scopeFactory,
        IOptions<SporeSyncOptions> options,
        ILogger<DownloadWorkerHostedService> logger)
    {
        _scopeFactory = scopeFactory;
        _options = options.Value;
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
        var downloader = scope.ServiceProvider.GetRequiredService<SftpFileDownloader>();
        var notifier = scope.ServiceProvider.GetRequiredService<ISyncDashboardNotifier>();

        var item = await queueRepository.ClaimNextAsync(cancellationToken);
        if (item is null || item.SyncRunId is null)
        {
            return false;
        }

        var job = await jobRepository.GetByIdAsync(item.JobId, cancellationToken)
            ?? throw new InvalidOperationException($"Job '{item.JobId}' was not found for queue item '{item.Id}'.");

        DownloadQueueItem updatedItem;
        if (item.IsGroup)
        {
            updatedItem = await ProcessGroupAsync(
                item,
                job,
                queueRepository,
                downloader,
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

        await notifier.NotifyQueueItemUpdatedAsync(updatedItem, cancellationToken);

        var run = await runRepository.RecalculateAggregatesAsync(item.SyncRunId.Value, cancellationToken);
        await notifier.NotifyRunUpdatedAsync(run, cancellationToken);

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

    private static async Task<DownloadQueueItem> ProcessSingleFileAsync(
        DownloadQueueItem item,
        SporeSyncJob job,
        IDownloadQueueItemRepository queueRepository,
        SftpFileDownloader downloader,
        ISyncDashboardNotifier notifier,
        CancellationToken cancellationToken)
    {
        var progress = new Progress<long>(bytes =>
        {
            // Best-effort progress update; do not block download thread
            _ = Task.Run(async () =>
            {
                try
                {
                    var partial = await queueRepository.UpdateProgressAsync(new UpdateDownloadQueueItemProgress
                    {
                        Id = item.Id,
                        Status = "downloading",
                        BytesDownloaded = bytes,
                        CurrentBytesPerSecond = null,
                        ErrorMessage = null
                    }, cancellationToken);
                    await notifier.NotifyQueueItemUpdatedAsync(partial, cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    // ignore
                }
                catch
                {
                    // best effort; ignore transient progress reporting errors
                }
            }, cancellationToken);
        });

        var result = await downloader.DownloadAsync(
            job.ConnectionProfileId,
            item.RemotePath,
            item.DestinationPath,
            progress,
            cancellationToken);

        return await queueRepository.UpdateProgressAsync(new UpdateDownloadQueueItemProgress
        {
            Id = item.Id,
            Status = result.Success ? "completed" : "failed",
            BytesDownloaded = result.BytesDownloaded,
            CurrentBytesPerSecond = result.BytesPerSecond,
            ErrorMessage = result.ErrorMessage
        }, cancellationToken);
    }

    private static async Task<DownloadQueueItem> ProcessGroupAsync(
        DownloadQueueItem groupItem,
        SporeSyncJob job,
        IDownloadQueueItemRepository queueRepository,
        SftpFileDownloader downloader,
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

        foreach (var leaf in leaves)
        {
            if (string.Equals(leaf.Status, "completed", StringComparison.OrdinalIgnoreCase))
            {
                groupBytesDownloaded += leaf.BytesDownloaded;
                continue;
            }

            var leafProgress = new Progress<long>(bytes =>
            {
                _ = Task.Run(async () =>
                {
                    try
                    {
                        var partial = await queueRepository.UpdateProgressAsync(new UpdateDownloadQueueItemProgress
                        {
                            Id = leaf.Id,
                            Status = "downloading",
                            BytesDownloaded = bytes,
                            CurrentBytesPerSecond = null,
                            ErrorMessage = null
                        }, cancellationToken);
                        await notifier.NotifyQueueItemUpdatedAsync(partial, cancellationToken);
                    }
                    catch (OperationCanceledException)
                    {
                    }
                    catch
                    {
                    }
                }, cancellationToken);
            });

            var leafResult = await downloader.DownloadAsync(
                job.ConnectionProfileId,
                leaf.RemotePath,
                leaf.DestinationPath,
                leafProgress,
                cancellationToken);

            var updatedLeaf = await queueRepository.UpdateProgressAsync(new UpdateDownloadQueueItemProgress
            {
                Id = leaf.Id,
                Status = leafResult.Success ? "completed" : "failed",
                BytesDownloaded = leafResult.BytesDownloaded,
                CurrentBytesPerSecond = leafResult.BytesPerSecond,
                ErrorMessage = leafResult.ErrorMessage
            }, cancellationToken);

            if (leafResult.Success)
            {
                groupBytesDownloaded += leafResult.BytesDownloaded;
                latestRate = leafResult.BytesPerSecond;
            }
            else
            {
                groupFailed = true;
            }
        }

        return await queueRepository.UpdateProgressAsync(new UpdateDownloadQueueItemProgress
        {
            Id = groupItem.Id,
            Status = groupFailed ? "failed" : "completed",
            BytesDownloaded = groupBytesDownloaded,
            CurrentBytesPerSecond = latestRate,
            ErrorMessage = groupFailed ? "One or more files in the group failed to download." : null
        }, cancellationToken);
    }
}
