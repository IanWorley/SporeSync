using SporeSync.Domain.Model;

namespace SporeSync.Domain.Interface;

public interface ISporeSyncJobRepository
{
    Task<IReadOnlyCollection<SporeSyncJob>> GetAllAsync(CancellationToken cancellationToken = default);

    Task<SporeSyncJob?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    Task<SporeSyncJob> UpsertAsync(UpsertSporeSyncJob job, CancellationToken cancellationToken = default);

    Task<IReadOnlyCollection<SporeSyncJob>> GetDueJobsAsync(CancellationToken cancellationToken = default);

    Task MarkPolledAsync(Guid id, CancellationToken cancellationToken = default);
}
