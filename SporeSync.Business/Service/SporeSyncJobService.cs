using SporeSync.Business.Interface;
using SporeSync.Business.Security;
using SporeSync.Domain.Interface;
using SporeSync.Domain.Model;

namespace SporeSync.Business.Service;

public sealed class SporeSyncJobService : ISporeSyncJobService
{
    private readonly ISporeSyncJobRepository _sporeSyncJobRepository;
    private readonly ISporeSyncRunRepository _sporeSyncRunRepository;
    private readonly LocalDestinationPathSandbox _destinationPathSandbox;

    public SporeSyncJobService(
        ISporeSyncJobRepository sporeSyncJobRepository,
        ISporeSyncRunRepository sporeSyncRunRepository,
        LocalDestinationPathSandbox destinationPathSandbox)
    {
        _sporeSyncJobRepository = sporeSyncJobRepository;
        _sporeSyncRunRepository = sporeSyncRunRepository;
        _destinationPathSandbox = destinationPathSandbox;
    }

    public Task<IReadOnlyCollection<SporeSyncJob>> GetConfiguredJobsAsync(
        CancellationToken cancellationToken = default)
    {
        return _sporeSyncJobRepository.GetAllAsync(cancellationToken);
    }

    public Task<SporeSyncJob?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return _sporeSyncJobRepository.GetByIdAsync(id, cancellationToken);
    }

    public Task<SporeSyncJob> UpsertAsync(
        UpsertSporeSyncJob job,
        CancellationToken cancellationToken = default)
    {
        var sandboxedJob = new UpsertSporeSyncJob
        {
            Id = job.Id,
            ConnectionProfileId = job.ConnectionProfileId,
            Name = job.Name,
            SourcePath = job.SourcePath,
            DestinationPath = _destinationPathSandbox.RequireContainedPath(
                job.DestinationPath,
                nameof(job.DestinationPath)),
            PollingIntervalSeconds = job.PollingIntervalSeconds,
            IsEnabled = job.IsEnabled
        };

        return _sporeSyncJobRepository.UpsertAsync(sandboxedJob, cancellationToken);
    }

    public async Task<DeleteSporeSyncJobStatus> DeleteAsync(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        var job = await _sporeSyncJobRepository.GetByIdAsync(id, cancellationToken);
        if (job is null)
        {
            return DeleteSporeSyncJobStatus.NotFound;
        }

        if (await _sporeSyncRunRepository.HasActiveRunAsync(id, cancellationToken))
        {
            return DeleteSporeSyncJobStatus.ActiveRunExists;
        }

        var deleted = await _sporeSyncJobRepository.DeleteAsync(id, cancellationToken);
        return deleted ? DeleteSporeSyncJobStatus.Deleted : DeleteSporeSyncJobStatus.NotFound;
    }
}
