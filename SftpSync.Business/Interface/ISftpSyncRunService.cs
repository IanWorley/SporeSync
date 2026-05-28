using SftpSync.Domain.Model;

namespace SftpSync.Business.Interface;

public interface ISftpSyncRunService
{
    Task<PagedResult<SftpSyncRun>> GetRunsAsync(
        RunQuery query,
        CancellationToken cancellationToken = default);

    Task<SftpSyncRun?> GetByIdAsync(
        Guid id,
        CancellationToken cancellationToken = default);
}
