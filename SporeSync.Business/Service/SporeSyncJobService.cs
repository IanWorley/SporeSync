using SporeSync.Business.Interface;
using SporeSync.Domain.Interface;
using SporeSync.Domain.Model;

namespace SporeSync.Business.Service;

public sealed class SporeSyncJobService : ISporeSyncJobService
{
    private readonly ISporeSyncJobRepository _sporeSyncJobRepository;

    public SporeSyncJobService(ISporeSyncJobRepository sporeSyncJobRepository)
    {
        _sporeSyncJobRepository = sporeSyncJobRepository;
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
        return _sporeSyncJobRepository.UpsertAsync(job, cancellationToken);
    }
}
