using System.Security.Cryptography;
using Microsoft.Extensions.Logging.Abstractions;
using SporeSync.Business.Interface;
using SporeSync.Business.Security;
using SporeSync.Business.Service;
using SporeSync.Business.Sftp;
using SporeSync.Domain.Interface;
using SporeSync.Domain.Model;

namespace SporeSync.Business.Tests;

public sealed class SftpConnectionTestServiceTests
{
    [Theory]
    [InlineData(SftpAuthenticationMethod.Password)]
    [InlineData(SftpAuthenticationMethod.PrivateKey)]
    public async Task ExistingProfile_UsesOnlyTheSelectedAuthenticationCredential(
        SftpAuthenticationMethod authenticationMethod)
    {
        var keyProvider = new EncryptionKeyProvider();
        keyProvider.Initialize(RandomNumberGenerator.GetBytes(32));
        var protector = new SecretProtector(keyProvider);
        var stored = new SftpConnectionProfile
        {
            Id = Guid.NewGuid(),
            Name = "stored",
            Host = "sftp.example.test",
            Port = 22,
            Username = "sync-user",
            EncryptedPassword = protector.Protect("stored-password"),
            EncryptedPrivateKey = protector.Protect("stored-private-key"),
            EncryptedPrivateKeyPassphrase = protector.Protect("stored-passphrase"),
            IsDefault = false
        };
        var repository = new FakeProfileRepository(stored);
        var factory = new CapturingSftpClientFactory();
        var service = new SftpConnectionTestService(
            repository,
            factory,
            protector,
            NullLogger<SftpConnectionTestService>.Instance);

        await service.TestAsync(new SftpConnectionTestRequest
        {
            ProfileId = stored.Id,
            Host = stored.Host,
            Port = stored.Port,
            Username = stored.Username,
            AuthenticationMethod = authenticationMethod,
            Password = authenticationMethod == SftpAuthenticationMethod.Password ? "replacement-password" : null,
            PrivateKey = authenticationMethod == SftpAuthenticationMethod.PrivateKey ? "replacement-private-key" : null
        });

        Assert.NotNull(factory.Profile);
        if (authenticationMethod == SftpAuthenticationMethod.Password)
        {
            Assert.Equal("replacement-password", protector.Unprotect(factory.Profile.EncryptedPassword!));
            Assert.Null(factory.Profile.EncryptedPrivateKey);
            Assert.Null(factory.Profile.EncryptedPrivateKeyPassphrase);
        }
        else
        {
            Assert.Null(factory.Profile.EncryptedPassword);
            Assert.Equal("replacement-private-key", protector.Unprotect(factory.Profile.EncryptedPrivateKey!));
            Assert.Equal("stored-passphrase", protector.Unprotect(factory.Profile.EncryptedPrivateKeyPassphrase!));
        }
    }

    private sealed class CapturingSftpClientFactory : ISftpClientFactory
    {
        public SftpConnectionProfile? Profile { get; private set; }

        public Task<IConnectedSftpClient> ConnectAsync(
            Guid connectionProfileId,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<IConnectedSftpClient> ConnectAsync(
            SftpConnectionProfile profile,
            CancellationToken cancellationToken = default)
        {
            Profile = profile;
            throw new System.Net.Sockets.SocketException();
        }
    }

    private sealed class FakeProfileRepository(SftpConnectionProfile profile) : ISftpConnectionProfileRepository
    {
        public Task<SftpConnectionProfile?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
            Task.FromResult<SftpConnectionProfile?>(profile.Id == id ? profile : null);

        public Task<SftpConnectionProfile> UpsertAsync(
            SftpConnectionProfile updated,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<IReadOnlyCollection<SftpConnectionProfile>> GetAllAsync(
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<bool> TryPinHostKeyFingerprintAsync(
            Guid id,
            string fingerprintSha256,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<bool> HasAnyEncryptedSecretsAsync(CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<SafeDeleteSftpConnectionProfileResult> SafeDeleteAsync(
            Guid id,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }
}
