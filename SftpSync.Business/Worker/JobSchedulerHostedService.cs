using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SftpSync.Domain.Interface;

namespace SftpSync.Business.Worker;

public sealed class JobSchedulerHostedService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly SftpSyncOptions _options;
    private readonly ILogger<JobSchedulerHostedService> _logger;

    public JobSchedulerHostedService(
        IServiceScopeFactory scopeFactory,
        IOptions<SftpSyncOptions> options,
        ILogger<JobSchedulerHostedService> logger)
    {
        _scopeFactory = scopeFactory;
        _options = options.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var interval = TimeSpan.FromSeconds(_options.SchedulerIntervalSeconds);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await PollDueJobsAsync(stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "Job scheduler tick failed.");
            }

            try
            {
                await Task.Delay(interval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    internal async Task PollDueJobsAsync(CancellationToken cancellationToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var jobRepository = scope.ServiceProvider.GetRequiredService<ISftpSyncJobRepository>();
        var runRepository = scope.ServiceProvider.GetRequiredService<ISftpSyncRunRepository>();
        var orchestrator = scope.ServiceProvider.GetRequiredService<ISyncRunOrchestrator>();
        var notifier = scope.ServiceProvider.GetRequiredService<Interface.ISyncDashboardNotifier>();

        var dueJobs = await jobRepository.GetDueJobsAsync(cancellationToken);
        if (dueJobs.Count == 0)
        {
            return;
        }

        var tasks = dueJobs.Select(job => ProcessDueJobAsync(
            job,
            jobRepository,
            runRepository,
            orchestrator,
            notifier,
            cancellationToken));

        await Task.WhenAll(tasks);
    }

    private async Task ProcessDueJobAsync(
        Domain.Model.SftpSyncJob job,
        ISftpSyncJobRepository jobRepository,
        ISftpSyncRunRepository runRepository,
        ISyncRunOrchestrator orchestrator,
        Interface.ISyncDashboardNotifier notifier,
        CancellationToken cancellationToken)
    {
        if (await runRepository.HasActiveRunAsync(job.Id, cancellationToken))
        {
            return;
        }

        await jobRepository.MarkPolledAsync(job.Id, cancellationToken);

        var run = await runRepository.CreateAsync(job.Id, cancellationToken);
        await notifier.NotifyRunUpdatedAsync(run, cancellationToken);
        await orchestrator.ScanAsync(job, run, cancellationToken);
    }
}
