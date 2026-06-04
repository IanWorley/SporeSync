using SporeSync.Business.Interface;
using SporeSync.Domain.Interface;
using SporeSync.Domain.Model;

namespace SporeSync.Business.Service;

public sealed class SporeSyncRunService : ISporeSyncRunService
{
    private readonly ISporeSyncRunRepository _repository;

    public SporeSyncRunService(ISporeSyncRunRepository repository)
    {
        _repository = repository;
    }

    public Task<PagedResult<SporeSyncRun>> GetRunsAsync(
        RunQuery query,
        CancellationToken cancellationToken = default)
    {
        return _repository.GetRunsAsync(query, cancellationToken);
    }

    public Task<SporeSyncRun?> GetByIdAsync(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        return _repository.GetByIdAsync(id, cancellationToken);
    }
}
