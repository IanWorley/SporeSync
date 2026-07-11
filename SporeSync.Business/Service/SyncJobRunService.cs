using Microsoft.Extensions.Options;
using SporeSync.Business.Interface;
using SporeSync.Business.Worker;
using SporeSync.Domain.Interface;

namespace SporeSync.Business.Service;

public sealed class SyncJobRunService : ISyncJobRunService
{
    private readonly ISporeSyncJobRepository _jobRepository;
    private readonly ISporeSyncRunRepository _runRepository;
    private readonly IManualRunQueue _manualRunQueue;
    private readonly ISyncDashboardNotifier _notifier;
    private readonly SporeSyncOptions _options;

    public SyncJobRunService(
        ISporeSyncJobRepository jobRepository,
        ISporeSyncRunRepository runRepository,
        IManualRunQueue manualRunQueue,
        ISyncDashboardNotifier notifier,
        IOptions<SporeSyncOptions> options)
    {
        _jobRepository = jobRepository;
        _runRepository = runRepository;
        _manualRunQueue = manualRunQueue;
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

        if (!_manualRunQueue.TryReserve(out var reservation))
        {
            return new SyncJobRunResult { Error = SyncJobRunError.QueueSaturated };
        }

        using (reservation)
        {
            // Creation is atomic: null means another caller (scheduler or a concurrent
            // manual trigger) created an active run first.
            var run = await _runRepository.TryCreateAsync(jobId, _options.RunScanLeaseSeconds, cancellationToken);
            if (run is null)
            {
                return new SyncJobRunResult { Error = SyncJobRunError.ActiveRunExists };
            }

            reservation!.Enqueue(new ManualRunWorkItem(jobId, run.Id));
            await _notifier.NotifyRunUpdatedAsync(run, cancellationToken);
            return new SyncJobRunResult { Run = run };
        }
    }
}
