using SftpSync.Business.Interface;
using SftpSync.Domain.Interface;
using SftpSync.Domain.Model;

namespace SftpSync.Business.Service;

public sealed class SftpSyncJobService : ISftpSyncJobService
{
    private readonly ISftpSyncJobRepository _sftpSyncJobRepository;

    public SftpSyncJobService(ISftpSyncJobRepository sftpSyncJobRepository)
    {
        _sftpSyncJobRepository = sftpSyncJobRepository;
    }

    public Task<IReadOnlyCollection<SftpSyncJob>> GetConfiguredJobsAsync(
        CancellationToken cancellationToken = default)
    {
        return _sftpSyncJobRepository.GetAllAsync(cancellationToken);
    }

    public Task<SftpSyncJob?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return _sftpSyncJobRepository.GetByIdAsync(id, cancellationToken);
    }

    public Task<SftpSyncJob> UpsertAsync(
        UpsertSftpSyncJob job,
        CancellationToken cancellationToken = default)
    {
        return _sftpSyncJobRepository.UpsertAsync(job, cancellationToken);
    }
}
