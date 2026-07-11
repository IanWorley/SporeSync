using SporeSync.Domain.Model;

namespace SporeSync.Domain.Interface;

public enum SafeDeleteSporeSyncJobResult
{
    Deleted,
    NotFound,
    ActiveRunExists
}

public interface ISporeSyncJobRepository
{
    Task<IReadOnlyCollection<SporeSyncJob>> GetAllAsync(CancellationToken cancellationToken = default);

    Task<SporeSyncJob?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    Task<SporeSyncJob> UpsertAsync(UpsertSporeSyncJob job, CancellationToken cancellationToken = default);

    Task<IReadOnlyCollection<SporeSyncJob>> GetDueJobsAsync(CancellationToken cancellationToken = default);

    Task MarkPolledAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes a job together with its runs and queue items. Returns false when the job does not exist.
    /// </summary>
    Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken = default);

    Task<SafeDeleteSporeSyncJobResult> SafeDeleteAsync(
        Guid id,
        CancellationToken cancellationToken = default);

    Task<int> CountByConnectionProfileAsync(Guid connectionProfileId, CancellationToken cancellationToken = default);
}
