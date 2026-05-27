using SftpSync.Domain.Model;

namespace SftpSync.Infrastructure.Interface;

public interface ISftpSyncJobRepository
{
    Task<IReadOnlyCollection<SftpSyncJob>> GetAllAsync(CancellationToken cancellationToken = default);
}
