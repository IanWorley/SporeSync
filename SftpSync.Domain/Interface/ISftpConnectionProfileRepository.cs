using SftpSync.Domain.Model;

namespace SftpSync.Domain.Interface;

public interface ISftpConnectionProfileRepository
{
    Task<IReadOnlyCollection<SftpConnectionProfile>> GetAllAsync(CancellationToken cancellationToken = default);

    Task<SftpConnectionProfile?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    Task<SftpConnectionProfile> UpsertAsync(
        SftpConnectionProfile profile,
        CancellationToken cancellationToken = default);
}
