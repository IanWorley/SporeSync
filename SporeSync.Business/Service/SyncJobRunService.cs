using Microsoft.Extensions.Options;
using SporeSync.Business.Interface;
using SporeSync.Business.Worker;
using SporeSync.Domain.Interface;

namespace SporeSync.Business.Service;

public sealed class SyncJobRunService : ISyncJobRunService
{
    private readonly ISporeSyncJobRepository _jobRepository;
    private readonly ISporeSyncRunRepository _runRepository;
    private readonly ISyncRunOrchestrator _orchestrator;
    private readonly ISyncDashboardNotifier _notifier;
    private readonly SporeSyncOptions _options;

    public SyncJobRunService(
        ISporeSyncJobRepository jobRepository,
        ISporeSyncRunRepository runRepository,
        ISyncRunOrchestrator orchestrator,
        ISyncDashboardNotifier notifier,
        IOptions<SporeSyncOptions> options)
    {
        _jobRepository = jobRepository;
        _runRepository = runRepository;
        _orchestrator = orchestrator;
        _notifier = notifier;
        _options = options.Value;
    }

    public async Task<SyncJobRunResult> TriggerManualRunAsync(
        Guid jobId,
        CancellationToken cancellationToken = default)
    {
        var job = await _jobRepository.GetByIdAsync(jobId, cancellationToken);
        if (job is null)
        {
            return new SyncJobRunResult { Error = SyncJobRunError.NotFound };
        }

        if (!job.IsEnabled)
        {
            return new SyncJobRunResult { Error = SyncJobRunError.Disabled };
        }

        // Creation is atomic: null means another caller (scheduler or a concurrent
        // manual trigger) created an active run first.
        var run = await _runRepository.TryCreateAsync(jobId, _options.RunScanLeaseSeconds, cancellationToken);
        if (run is null)
        {
            return new SyncJobRunResult { Error = SyncJobRunError.ActiveRunExists };
        }

        await _notifier.NotifyRunUpdatedAsync(run, cancellationToken);

        _ = Task.Run(async () =>
        {
            try
            {
                await _orchestrator.ScanAsync(job, run, CancellationToken.None);
            }
            catch
            {
                // Errors are persisted on the run by the orchestrator.
            }
        }, CancellationToken.None);

        return new SyncJobRunResult { Run = run };
    }
}
