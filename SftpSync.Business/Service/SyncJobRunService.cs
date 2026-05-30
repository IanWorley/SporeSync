using SftpSync.Business.Interface;
using SftpSync.Business.Worker;
using SftpSync.Domain.Interface;

namespace SftpSync.Business.Service;

public sealed class SyncJobRunService : ISyncJobRunService
{
    private readonly ISftpSyncJobRepository _jobRepository;
    private readonly ISftpSyncRunRepository _runRepository;
    private readonly ISyncRunOrchestrator _orchestrator;
    private readonly ISyncDashboardNotifier _notifier;

    public SyncJobRunService(
        ISftpSyncJobRepository jobRepository,
        ISftpSyncRunRepository runRepository,
        ISyncRunOrchestrator orchestrator,
        ISyncDashboardNotifier notifier)
    {
        _jobRepository = jobRepository;
        _runRepository = runRepository;
        _orchestrator = orchestrator;
        _notifier = notifier;
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

        if (await _runRepository.HasActiveRunAsync(jobId, cancellationToken))
        {
            return new SyncJobRunResult { Error = SyncJobRunError.ActiveRunExists };
        }

        var run = await _runRepository.CreateAsync(jobId, cancellationToken);
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
