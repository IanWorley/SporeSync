using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SporeSync.Business.Interface;
using SporeSync.Domain.Interface;

namespace SporeSync.Business.Worker;

/// <summary>
/// Crash-recovery sweep:
/// * requeues claimed queue items whose lease expired (crashed/hung worker);
/// * reaps orphaned runs (queued/scanning runs with an expired lease are failed,
///   downloading runs with no pending items are finalized as completed).
/// Runs once at startup — before the scheduler and download worker start, and
/// ignoring leases since no in-process work can be in flight yet — and then
/// periodically while the host is running.
/// </summary>
public sealed class QueueRecoveryHostedService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly SporeSyncOptions _options;
    private readonly ILogger<QueueRecoveryHostedService> _logger;

    public QueueRecoveryHostedService(
        IServiceScopeFactory scopeFactory,
        IOptions<SporeSyncOptions> options,
        ILogger<QueueRecoveryHostedService> logger)
    {
        _scopeFactory = scopeFactory;
        _options = options.Value;
        _logger = logger;
    }

    public override async Task StartAsync(CancellationToken cancellationToken)
    {
        // Run the startup sweep to completion before other hosted services start
        // claiming work (hosted services start sequentially in registration order).
        try
        {
            await SweepAsync(ignoreLeases: true, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Startup recovery sweep failed; the periodic sweep will retry.");
        }

        await base.StartAsync(cancellationToken);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var interval = TimeSpan.FromSeconds(_options.RecoverySweepIntervalSeconds);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(interval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }

            try
            {
                await SweepAsync(ignoreLeases: false, stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "Recovery sweep failed.");
            }
        }
    }

    public async Task SweepAsync(bool ignoreLeases, CancellationToken cancellationToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var queueRepository = scope.ServiceProvider.GetRequiredService<IDownloadQueueItemRepository>();
        var runRepository = scope.ServiceProvider.GetRequiredService<ISporeSyncRunRepository>();
        var notifier = scope.ServiceProvider.GetRequiredService<ISyncDashboardNotifier>();

        var requeuedItems = await queueRepository.RequeueStaleAsync(ignoreLeases, cancellationToken);
        if (requeuedItems.Count > 0)
        {
            _logger.LogWarning(
                "Recovery sweep requeued {Count} stale downloading queue item(s).",
                requeuedItems.Count);

            foreach (var item in requeuedItems)
            {
                await notifier.NotifyQueueItemUpdatedAsync(item, cancellationToken);
            }

            // Requeued items reset their progress; refresh the aggregates of the
            // affected runs so the dashboard reflects reality.
            foreach (var runId in requeuedItems
                .Where(item => item.SyncRunId is not null)
                .Select(item => item.SyncRunId!.Value)
                .Distinct())
            {
                var run = await runRepository.RecalculateAggregatesAsync(runId, cancellationToken);
                await notifier.NotifyRunUpdatedAsync(run, cancellationToken);
            }
        }

        var reapedRuns = await runRepository.ReapOrphanedAsync(ignoreLeases, cancellationToken);
        if (reapedRuns.Count > 0)
        {
            _logger.LogWarning(
                "Recovery sweep reaped {Count} orphaned run(s).",
                reapedRuns.Count);

            foreach (var run in reapedRuns)
            {
                await notifier.NotifyRunUpdatedAsync(run, cancellationToken);
            }
        }
    }
}
