using SporeSync.Domain.Model;

namespace SporeSync.Business.Interface;

public interface ISftpConnectionProfileService
{
    Task<IReadOnlyCollection<SftpConnectionProfile>> GetAllAsync(CancellationToken cancellationToken = default);

    Task<SftpConnectionProfile?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    Task<SftpConnectionProfile> UpsertAsync(
        UpsertSftpConnectionProfile profile,
        CancellationToken cancellationToken = default);
}
