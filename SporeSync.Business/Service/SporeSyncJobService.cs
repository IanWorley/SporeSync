using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
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
    private readonly ILogger<SporeSyncJobService> _logger;

    public SporeSyncJobService(
        ISporeSyncJobRepository sporeSyncJobRepository,
        ISporeSyncRunRepository sporeSyncRunRepository,
        LocalDestinationPathSandbox destinationPathSandbox,
        ILogger<SporeSyncJobService>? logger = null)
    {
        _sporeSyncJobRepository = sporeSyncJobRepository;
        _sporeSyncRunRepository = sporeSyncRunRepository;
        _destinationPathSandbox = destinationPathSandbox;
        _logger = logger ?? NullLogger<SporeSyncJobService>.Instance;
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

        var result = await _sporeSyncJobRepository.SafeDeleteAsync(id, cancellationToken);
        if (result == SafeDeleteSporeSyncJobResult.Deleted)
        {
            _logger.LogInformation(
                "Configuration audit: deleted sync job {JobId} ({JobName}); history removed and local files retained",
                job.Id,
                job.Name);
        }

        return result switch
        {
            SafeDeleteSporeSyncJobResult.Deleted => DeleteSporeSyncJobStatus.Deleted,
            SafeDeleteSporeSyncJobResult.ActiveRunExists => DeleteSporeSyncJobStatus.ActiveRunExists,
            _ => DeleteSporeSyncJobStatus.NotFound
        };
    }
}
