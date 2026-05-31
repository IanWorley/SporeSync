using SporeSync.Domain.Model;

namespace SporeSync.Business.Interface;

public interface ISporeSyncJobService
{
    Task<IReadOnlyCollection<SporeSyncJob>> GetConfiguredJobsAsync(CancellationToken cancellationToken = default);

    Task<SporeSyncJob?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    Task<SporeSyncJob> UpsertAsync(UpsertSporeSyncJob job, CancellationToken cancellationToken = default);
}
