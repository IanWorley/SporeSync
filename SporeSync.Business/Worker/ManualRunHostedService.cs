using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SporeSync.Business.Interface;
using SporeSync.Domain.Interface;
using SporeSync.Domain.Model;

namespace SporeSync.Business.Worker;

public sealed class ManualRunHostedService : BackgroundService
{
    private readonly ManualRunQueue _queue;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<ManualRunHostedService> _logger;
    private readonly SporeSyncOptions _options;

    public ManualRunHostedService(
        ManualRunQueue queue,
        IServiceScopeFactory scopeFactory,
        IOptions<SporeSyncOptions> options,
        ILogger<ManualRunHostedService> logger)
    {
        _queue = queue;
        _scopeFactory = scopeFactory;
        _options = options.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var renewalCts = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
        var leaseRenewalTask = RenewQueuedLeasesAsync(renewalCts.Token);
        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                var item = await _queue.ReadAsync(stoppingToken);
                await ProcessWorkItemAsync(item, stoppingToken);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
        finally
        {
            renewalCts.Cancel();
            await leaseRenewalTask;
            while (_queue.TryRead(out var pending))
            {
                await TerminateRunAsync(pending!.RunId, "cancelled", null);
            }
        }
    }

    private async Task RenewQueuedLeasesAsync(CancellationToken stoppingToken)
    {
        var interval = TimeSpan.FromSeconds(Math.Max(1, _options.RunScanLeaseSeconds / 3.0));
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(interval, stoppingToken);
                await using var scope = _scopeFactory.CreateAsyncScope();
                var repository = scope.ServiceProvider.GetRequiredService<ISporeSyncRunRepository>();
                foreach (var runId in _queue.GetQueuedRunIds())
                {
                    await repository.RenewLeaseAsync(runId, _options.RunScanLeaseSeconds, stoppingToken);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Queued manual-run lease renewal failed; retrying.");
            }
        }
    }

    internal async Task ProcessWorkItemAsync(ManualRunWorkItem item, CancellationToken stoppingToken)
    {
        try
        {
            await using var scope = _scopeFactory.CreateAsyncScope();
            var jobRepository = scope.ServiceProvider.GetRequiredService<ISporeSyncJobRepository>();
            var runRepository = scope.ServiceProvider.GetRequiredService<ISporeSyncRunRepository>();
            var orchestrator = scope.ServiceProvider.GetRequiredService<ISyncRunOrchestrator>();

            var job = await jobRepository.GetByIdAsync(item.JobId, stoppingToken)
                ?? throw new InvalidOperationException($"Sync job '{item.JobId}' no longer exists.");
            var run = await runRepository.GetByIdAsync(item.RunId, stoppingToken)
                ?? throw new InvalidOperationException($"Sync run '{item.RunId}' no longer exists.");

            await orchestrator.ScanAsync(job, run, stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            _logger.LogInformation("Manual scan for run {RunId} stopped during application shutdown.", item.RunId);
            await TerminateRunAsync(item.RunId, "cancelled", null);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Manual scan failed unexpectedly for job {JobId} run {RunId}.", item.JobId, item.RunId);
            await TerminateRunAsync(item.RunId, "failed", ex.Message);
        }
    }

    private async Task TerminateRunAsync(Guid runId, string status, string? errorMessage)
    {
        try
        {
            await using var scope = _scopeFactory.CreateAsyncScope();
            var runRepository = scope.ServiceProvider.GetRequiredService<ISporeSyncRunRepository>();
            var notifier = scope.ServiceProvider.GetRequiredService<ISyncDashboardNotifier>();
            var run = await runRepository.UpdateStatusAsync(new UpdateSporeSyncRunStatus
            {
                Id = runId,
                Status = status,
                ErrorMessage = errorMessage
            }, CancellationToken.None);
            await notifier.NotifyRunUpdatedAsync(run, CancellationToken.None);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Could not persist terminal status {Status} for manual run {RunId}.", status, runId);
        }
    }
}
