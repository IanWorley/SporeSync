using SftpSync.Business.Interface;
using SftpSync.Domain.Interface;
using SftpSync.Domain.Model;

namespace SftpSync.Business.Service;

public sealed class SftpSyncRunService : ISftpSyncRunService
{
    private readonly ISftpSyncRunRepository _repository;

    public SftpSyncRunService(ISftpSyncRunRepository repository)
    {
        _repository = repository;
    }

    public Task<PagedResult<SftpSyncRun>> GetRunsAsync(
        RunQuery query,
        CancellationToken cancellationToken = default)
    {
        return _repository.GetRunsAsync(query, cancellationToken);
    }

    public Task<SftpSyncRun?> GetByIdAsync(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        return _repository.GetByIdAsync(id, cancellationToken);
    }
}
