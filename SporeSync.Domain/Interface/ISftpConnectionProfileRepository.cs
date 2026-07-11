using SporeSync.Domain.Model;

namespace SporeSync.Domain.Interface;

public enum SafeDeleteSftpConnectionProfileResult
{
    Deleted,
    NotFound,
    InUse
}

public interface ISftpConnectionProfileRepository
{
    Task<IReadOnlyCollection<SftpConnectionProfile>> GetAllAsync(CancellationToken cancellationToken = default);

    Task<SftpConnectionProfile?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    Task<SftpConnectionProfile> UpsertAsync(
        SftpConnectionProfile profile,
        CancellationToken cancellationToken = default);

    Task<bool> HasAnyEncryptedSecretsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes a connection profile. Returns false when the profile does not exist.
    /// </summary>
    Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken = default);

    Task<SafeDeleteSftpConnectionProfileResult> SafeDeleteAsync(
        Guid id,
        CancellationToken cancellationToken = default);
}
