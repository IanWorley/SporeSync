using SporeSync.Domain.Model;

namespace SporeSync.Business.Interface;

public enum DeleteSporeSyncJobStatus
{
    Deleted,
    NotFound,
    ActiveRunExists
}

public interface ISporeSyncJobService
{
    Task<IReadOnlyCollection<SporeSyncJob>> GetConfiguredJobsAsync(CancellationToken cancellationToken = default);

    Task<SporeSyncJob?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    Task<SporeSyncJob> UpsertAsync(UpsertSporeSyncJob job, CancellationToken cancellationToken = default);

    Task<DeleteSporeSyncJobStatus> DeleteAsync(Guid id, CancellationToken cancellationToken = default);
}
