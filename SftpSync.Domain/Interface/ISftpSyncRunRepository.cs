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

    Task<SftpSyncRun> CreateAsync(Guid jobId, CancellationToken cancellationToken = default);

    Task<SftpSyncRun> UpdateStatusAsync(
        UpdateSftpSyncRunStatus update,
        CancellationToken cancellationToken = default);

    Task<bool> HasActiveRunAsync(Guid jobId, CancellationToken cancellationToken = default);

    Task<SftpSyncRun> RecalculateAggregatesAsync(
        Guid runId,
        CancellationToken cancellationToken = default);

    Task<bool> HasPendingDownloadsAsync(Guid runId, CancellationToken cancellationToken = default);
}
