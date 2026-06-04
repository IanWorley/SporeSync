using SporeSync.Domain.Model;

namespace SporeSync.Business.Interface;

public interface ISporeSyncRunService
{
    Task<PagedResult<SporeSyncRun>> GetRunsAsync(
        RunQuery query,
        CancellationToken cancellationToken = default);

    Task<SporeSyncRun?> GetByIdAsync(
        Guid id,
        CancellationToken cancellationToken = default);
}
