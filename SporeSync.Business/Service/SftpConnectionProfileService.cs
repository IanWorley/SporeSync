using SporeSync.Business.Interface;
using SporeSync.Business.Sftp;
using SporeSync.Domain.Interface;
using SporeSync.Domain.Model;

namespace SporeSync.Business.Service;

public sealed class SftpConnectionProfileService : ISftpConnectionProfileService
{
    private readonly ISftpConnectionProfileRepository _repository;
    private readonly ISecretProtector _secretProtector;

    public SftpConnectionProfileService(
        ISftpConnectionProfileRepository repository,
        ISecretProtector secretProtector)
    {
        _repository = repository;
        _secretProtector = secretProtector;
    }

    public Task<IReadOnlyCollection<SftpConnectionProfile>> GetAllAsync(
        CancellationToken cancellationToken = default)
    {
        return _repository.GetAllAsync(cancellationToken);
    }

    public Task<SftpConnectionProfile?> GetByIdAsync(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        return _repository.GetByIdAsync(id, cancellationToken);
    }

    public async Task<SftpConnectionProfile> UpsertAsync(
        UpsertSftpConnectionProfile profile,
        CancellationToken cancellationToken = default)
    {
        SftpConnectionProfile? existingProfile = null;
        if (profile.Id is Guid id)
        {
            existingProfile = await _repository.GetByIdAsync(id, cancellationToken);
        }

        var encryptedPassword = ProtectOptional(profile.Password) ?? existingProfile?.EncryptedPassword;
        var encryptedPrivateKey = ProtectOptional(profile.PrivateKey) ?? existingProfile?.EncryptedPrivateKey;
        var encryptedPrivateKeyPassphrase = ProtectOptional(profile.PrivateKeyPassphrase)
            ?? existingProfile?.EncryptedPrivateKeyPassphrase;

        if (string.IsNullOrWhiteSpace(encryptedPassword) && string.IsNullOrWhiteSpace(encryptedPrivateKey))
        {
            throw new InvalidOperationException("An SFTP password or private key is required.");
        }

        var protectedProfile = new SftpConnectionProfile
        {
            Id = profile.Id ?? Guid.NewGuid(),
            Name = profile.Name,
            Host = profile.Host,
            Port = profile.Port,
            Username = profile.Username,
            EncryptedPassword = encryptedPassword,
            EncryptedPrivateKey = encryptedPrivateKey,
            EncryptedPrivateKeyPassphrase = encryptedPrivateKeyPassphrase,
            HostKeyFingerprintSha256 = ResolveHostKeyFingerprint(
                profile.HostKeyFingerprintSha256,
                existingProfile?.HostKeyFingerprintSha256),
            IsDefault = profile.IsDefault
        };

        return await _repository.UpsertAsync(protectedProfile, cancellationToken);
    }

    private static string? ResolveHostKeyFingerprint(string? requested, string? existing)
    {
        // Null preserves the stored pin; an explicit blank clears it (re-enabling
        // trust-on-first-use); any other value replaces the pin after normalization.
        if (requested is null)
        {
            return existing;
        }

        return string.IsNullOrWhiteSpace(requested)
            ? null
            : SshHostKeyFingerprint.Normalize(requested);
    }

    private string? ProtectOptional(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : _secretProtector.Protect(value);
    }
}
