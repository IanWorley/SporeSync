using SftpSync.Domain.Model;
using SftpSync.Infrastructure.Interface;

namespace SftpSync.Infrastructure.Repository;

public sealed class SftpSyncJobRepository : ISftpSyncJobRepository
{
    public Task<IReadOnlyCollection<SftpSyncJob>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        IReadOnlyCollection<SftpSyncJob> jobs = [];

        return Task.FromResult(jobs);
    }
}
