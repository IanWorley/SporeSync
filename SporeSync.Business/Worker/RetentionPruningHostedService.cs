using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SporeSync.Business.Observability;
using SporeSync.Domain.Interface;

namespace SporeSync.Business.Worker;

/// <summary>
/// Periodically prunes terminal sync runs older than the configured retention
/// window and stale remote-deleted queue markers, so the runs and queue tables
/// do not grow without bound on long-lived deployments.
/// </summary>
public sealed class RetentionPruningHostedService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly SporeSyncOptions _options;
    private readonly SporeSyncMetrics _metrics;
    private readonly ILogger<RetentionPruningHostedService> _logger;

    public RetentionPruningHostedService(
        IServiceScopeFactory scopeFactory,
        IOptions<SporeSyncOptions> options,
        SporeSyncMetrics metrics,
        ILogger<RetentionPruningHostedService> logger)
    {
        _scopeFactory = scopeFactory;
        _options = options.Value;
        _metrics = metrics;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (_options.RunHistoryRetentionDays <= 0)
        {
            _logger.LogInformation(
                "Sync history retention pruning is disabled (SporeSync:RunHistoryRetentionDays is 0).");
            return;
        }

        var sweepInterval = TimeSpan.FromHours(_options.RetentionSweepIntervalHours);
        _logger.LogInformation(
            "Sync history retention pruning enabled: retaining {RetentionDays} days, sweeping every {SweepIntervalHours} hours.",
            _options.RunHistoryRetentionDays,
            _options.RetentionSweepIntervalHours);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await PruneOnceAsync(stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "Sync history retention sweep failed.");
            }

            try
            {
                await Task.Delay(sweepInterval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    internal async Task PruneOnceAsync(CancellationToken cancellationToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var runRepository = scope.ServiceProvider.GetRequiredService<ISporeSyncRunRepository>();

        var cutoff = DateTimeOffset.UtcNow.AddDays(-_options.RunHistoryRetentionDays);
        var result = await runRepository.PruneHistoryAsync(cutoff, cancellationToken);

        _metrics.RecordRetentionPruned(result.PrunedRunCount, result.PrunedQueueItemCount);

        if (result.PrunedRunCount > 0 || result.PrunedQueueItemCount > 0)
        {
            _logger.LogInformation(
                "Retention sweep pruned {PrunedRunCount} runs and {PrunedQueueItemCount} stale queue items older than {Cutoff}.",
                result.PrunedRunCount,
                result.PrunedQueueItemCount,
                cutoff);
        }
    }
}
