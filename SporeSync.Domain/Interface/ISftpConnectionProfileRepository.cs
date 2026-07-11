using SporeSync.Domain.Model;

namespace SporeSync.Domain.Interface;

public interface ISftpConnectionProfileRepository
{
    Task<IReadOnlyCollection<SftpConnectionProfile>> GetAllAsync(CancellationToken cancellationToken = default);

    Task<SftpConnectionProfile?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    Task<SftpConnectionProfile> UpsertAsync(
        SftpConnectionProfile profile,
        CancellationToken cancellationToken = default);

    Task<bool> TryPinHostKeyFingerprintAsync(
        Guid id,
        string fingerprintSha256,
        CancellationToken cancellationToken = default);

    Task<bool> HasAnyEncryptedSecretsAsync(CancellationToken cancellationToken = default);
}
