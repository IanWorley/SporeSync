using System.ComponentModel.DataAnnotations;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using SporeSync.Business.Interface;
using SporeSync.Business.Sftp;
using SporeSync.Domain.Interface;
using SporeSync.Domain.Model;

namespace SporeSync.Business.Service;

public sealed class SftpConnectionProfileService : ISftpConnectionProfileService
{
    private readonly ISftpConnectionProfileRepository _repository;
    private readonly ISporeSyncJobRepository _jobRepository;
    private readonly ISecretProtector _secretProtector;
    private readonly ILogger<SftpConnectionProfileService> _logger;

    public SftpConnectionProfileService(
        ISftpConnectionProfileRepository repository,
        ISporeSyncJobRepository jobRepository,
        ISecretProtector secretProtector,
        ILogger<SftpConnectionProfileService>? logger = null)
    {
        _repository = repository;
        _jobRepository = jobRepository;
        _secretProtector = secretProtector;
        _logger = logger ?? NullLogger<SftpConnectionProfileService>.Instance;
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

        var (encryptedPassword, encryptedPrivateKey, encryptedPrivateKeyPassphrase) =
            ResolveAuthentication(profile, existingProfile);

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
            TrustedHostKeyFingerprintsSha256 = ResolveHostKeyFingerprints(
                profile.TrustedHostKeyFingerprintsSha256,
                existingProfile?.TrustedHostKeyFingerprintsSha256),
            IsDefault = profile.IsDefault
        };

        return await _repository.UpsertAsync(protectedProfile, cancellationToken);
    }

    private (string? Password, string? PrivateKey, string? Passphrase) ResolveAuthentication(
        UpsertSftpConnectionProfile requested,
        SftpConnectionProfile? existing)
    {
        if (requested.AuthenticationMethod == SftpAuthenticationMethod.Password)
        {
            var password = ProtectOptional(requested.Password) ?? existing?.EncryptedPassword;
            if (string.IsNullOrWhiteSpace(password))
            {
                throw new ValidationException("A password is required for password authentication.");
            }

            return (password, null, null);
        }

        if (requested.AuthenticationMethod != SftpAuthenticationMethod.PrivateKey)
        {
            throw new ValidationException("A supported SFTP authentication method is required.");
        }

        var privateKey = ProtectOptional(requested.PrivateKey) ?? existing?.EncryptedPrivateKey;
        if (string.IsNullOrWhiteSpace(privateKey))
        {
            throw new ValidationException("A private key is required for private key authentication.");
        }

        var passphrase = requested.RemovePrivateKeyPassphrase
            ? null
            : ProtectOptional(requested.PrivateKeyPassphrase) ?? existing?.EncryptedPrivateKeyPassphrase;

        return (null, privateKey, passphrase);
    }

    private static IReadOnlyList<string> ResolveHostKeyFingerprints(
        IReadOnlyList<string>? requested,
        IReadOnlyList<string>? existing)
    {
        if (requested is null)
        {
            return existing ?? [];
        }

        return requested
            .Select(SshHostKeyFingerprint.Normalize)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
    }

    public async Task<DeleteSftpConnectionProfileStatus> DeleteAsync(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        var profile = await _repository.GetByIdAsync(id, cancellationToken);
        if (profile is null)
        {
            return DeleteSftpConnectionProfileStatus.NotFound;
        }

        var jobCount = await _jobRepository.CountByConnectionProfileAsync(id, cancellationToken);
        if (jobCount > 0)
        {
            return DeleteSftpConnectionProfileStatus.InUse;
        }

        var result = await _repository.SafeDeleteAsync(id, cancellationToken);
        if (result == SafeDeleteSftpConnectionProfileResult.Deleted)
        {
            _logger.LogInformation(
                "Configuration audit: deleted SFTP connection profile {ProfileId} ({ProfileName}) and its stored credentials",
                profile.Id,
                profile.Name);
        }

        return result switch
        {
            SafeDeleteSftpConnectionProfileResult.Deleted => DeleteSftpConnectionProfileStatus.Deleted,
            SafeDeleteSftpConnectionProfileResult.InUse => DeleteSftpConnectionProfileStatus.InUse,
            _ => DeleteSftpConnectionProfileStatus.NotFound
        };
    }

    private string? ProtectOptional(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : _secretProtector.Protect(value);
    }
}
