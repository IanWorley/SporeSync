using SftpSync.Domain.Model;

namespace SftpSync.Domain.Interface;

public interface ISftpSyncRunRepository
{
    Task<PagedResult<SftpSyncRun>> GetRunsAsync(
        RunQuery query,
        CancellationToken cancellationToken = default);

    Task<SftpSyncRun?> GetByIdAsync(
        Guid id,
        CancellationToken cancellationToken = default);
}
