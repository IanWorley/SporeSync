using SftpSync.Domain.Model;

namespace SftpSync.Domain.Interface;

public interface ISftpSyncJobRepository
{
    Task<IReadOnlyCollection<SftpSyncJob>> GetAllAsync(CancellationToken cancellationToken = default);

    Task<SftpSyncJob?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    Task<SftpSyncJob> UpsertAsync(UpsertSftpSyncJob job, CancellationToken cancellationToken = default);
}
