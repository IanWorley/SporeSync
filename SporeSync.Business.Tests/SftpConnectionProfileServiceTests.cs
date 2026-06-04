using SporeSync.Business.Interface;
using SporeSync.Business.Service;
using SporeSync.Domain.Interface;
using SporeSync.Domain.Model;

namespace SporeSync.Business.Tests;

public sealed class SftpConnectionProfileServiceTests
{
    [Fact]
    public async Task UpsertAsync_Throws_WhenPasswordAndPrivateKeyAreMissing()
    {
        var repository = new RecordingSftpConnectionProfileRepository();
        var secretProtector = new RecordingSecretProtector();
        var service = new SftpConnectionProfileService(repository, secretProtector);

        var profile = new UpsertSftpConnectionProfile
        {
            Name = "default",
            Host = "sftp.example.com",
            Username = "sync-user"
        };

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.UpsertAsync(profile));

        Assert.Equal("An SFTP password or private key is required.", exception.Message);
        Assert.Null(repository.LastUpsertedProfile);
        Assert.Empty(secretProtector.ProtectedValues);
    }

    [Fact]
    public async Task UpsertAsync_ProtectsSecretsAndPersistsProfile()
    {
        var repository = new RecordingSftpConnectionProfileRepository();
        var secretProtector = new RecordingSecretProtector();
        var service = new SftpConnectionProfileService(repository, secretProtector);
        var id = Guid.NewGuid();

        var result = await service.UpsertAsync(
            new UpsertSftpConnectionProfile
            {
                Id = id,
                Name = "production",
                Host = "sftp.example.com",
                Port = 2222,
                Username = "sync-user",
                Password = "password-1",
                PrivateKey = "private-key",
                PrivateKeyPassphrase = "passphrase",
                IsDefault = false
            });

        Assert.Same(repository.LastUpsertedProfile, result);
        Assert.NotNull(repository.LastUpsertedProfile);
        Assert.Equal(id, repository.LastUpsertedProfile.Id);
        Assert.Equal("production", repository.LastUpsertedProfile.Name);
        Assert.Equal("sftp.example.com", repository.LastUpsertedProfile.Host);
        Assert.Equal(2222, repository.LastUpsertedProfile.Port);
        Assert.Equal("sync-user", repository.LastUpsertedProfile.Username);
        Assert.Equal("protected:password-1", repository.LastUpsertedProfile.EncryptedPassword);
        Assert.Equal("protected:private-key", repository.LastUpsertedProfile.EncryptedPrivateKey);
        Assert.Equal("protected:passphrase", repository.LastUpsertedProfile.EncryptedPrivateKeyPassphrase);
        Assert.False(repository.LastUpsertedProfile.IsDefault);
        Assert.Equal(["password-1", "private-key", "passphrase"], secretProtector.ProtectedValues);
    }

    [Fact]
    public async Task UpsertAsync_GeneratesIdAndSkipsBlankOptionalSecrets()
    {
        var repository = new RecordingSftpConnectionProfileRepository();
        var secretProtector = new RecordingSecretProtector();
        var service = new SftpConnectionProfileService(repository, secretProtector);

        await service.UpsertAsync(
            new UpsertSftpConnectionProfile
            {
                Name = "default",
                Host = "sftp.example.com",
                Username = "sync-user",
                Password = " password ",
                PrivateKeyPassphrase = " "
            });

        Assert.NotNull(repository.LastUpsertedProfile);
        Assert.NotEqual(Guid.Empty, repository.LastUpsertedProfile.Id);
        Assert.Equal("protected: password ", repository.LastUpsertedProfile.EncryptedPassword);
        Assert.Null(repository.LastUpsertedProfile.EncryptedPrivateKey);
        Assert.Null(repository.LastUpsertedProfile.EncryptedPrivateKeyPassphrase);
        Assert.Equal([" password "], secretProtector.ProtectedValues);
    }

    [Fact]
    public async Task UpsertAsync_PreservesExistingSecrets_WhenEditingWithoutReplacement()
    {
        var profileId = Guid.NewGuid();
        var repository = new RecordingSftpConnectionProfileRepository
        {
            ProfileById = new SftpConnectionProfile
            {
                Id = profileId,
                Name = "existing",
                Host = "old.example.com",
                Port = 22,
                Username = "old-user",
                EncryptedPassword = "existing-password",
                EncryptedPrivateKey = "existing-key",
                EncryptedPrivateKeyPassphrase = "existing-passphrase",
                IsDefault = true
            }
        };
        var secretProtector = new RecordingSecretProtector();
        var service = new SftpConnectionProfileService(repository, secretProtector);

        await service.UpsertAsync(
            new UpsertSftpConnectionProfile
            {
                Id = profileId,
                Name = "updated",
                Host = "new.example.com",
                Port = 2222,
                Username = "new-user",
                Password = "",
                PrivateKey = " ",
                PrivateKeyPassphrase = null,
                IsDefault = false
            });

        Assert.NotNull(repository.LastUpsertedProfile);
        Assert.Equal("existing-password", repository.LastUpsertedProfile.EncryptedPassword);
        Assert.Equal("existing-key", repository.LastUpsertedProfile.EncryptedPrivateKey);
        Assert.Equal("existing-passphrase", repository.LastUpsertedProfile.EncryptedPrivateKeyPassphrase);
        Assert.Empty(secretProtector.ProtectedValues);
    }

    [Fact]
    public async Task ReadMethods_DelegateToRepository()
    {
        var repository = new RecordingSftpConnectionProfileRepository();
        var service = new SftpConnectionProfileService(repository, new RecordingSecretProtector());
        var profileId = Guid.NewGuid();
        var cancellationToken = new CancellationTokenSource().Token;

        var allProfiles = await service.GetAllAsync(cancellationToken);
        var profile = await service.GetByIdAsync(profileId, cancellationToken);

        Assert.Same(repository.Profiles, allProfiles);
        Assert.Same(repository.ProfileById, profile);
        Assert.Equal(profileId, repository.LastRequestedId);
        Assert.Equal(cancellationToken, repository.LastCancellationToken);
    }

    private sealed class RecordingSecretProtector : ISecretProtector
    {
        public List<string> ProtectedValues { get; } = [];

        public string Protect(string plaintext)
        {
            ProtectedValues.Add(plaintext);
            return $"protected:{plaintext}";
        }

        public string Unprotect(string protectedValue)
        {
            throw new NotSupportedException();
        }
    }

    private sealed class RecordingSftpConnectionProfileRepository : ISftpConnectionProfileRepository
    {
        public IReadOnlyCollection<SftpConnectionProfile> Profiles { get; } =
        [
            new SftpConnectionProfile
            {
                Id = Guid.NewGuid(),
                Name = "default",
                Host = "sftp.example.com",
                Port = 22,
                Username = "sync-user",
                IsDefault = true
            }
        ];

        public SftpConnectionProfile ProfileById { get; set; } = new()
        {
            Id = Guid.NewGuid(),
            Name = "requested",
            Host = "sftp.example.com",
            Port = 22,
            Username = "sync-user",
            IsDefault = true
        };

        public Guid? LastRequestedId { get; private set; }

        public CancellationToken LastCancellationToken { get; private set; }

        public SftpConnectionProfile? LastUpsertedProfile { get; private set; }

        public Task<IReadOnlyCollection<SftpConnectionProfile>> GetAllAsync(
            CancellationToken cancellationToken = default)
        {
            LastCancellationToken = cancellationToken;
            return Task.FromResult(Profiles);
        }

        public Task<SftpConnectionProfile?> GetByIdAsync(
            Guid id,
            CancellationToken cancellationToken = default)
        {
            LastRequestedId = id;
            LastCancellationToken = cancellationToken;
            return Task.FromResult<SftpConnectionProfile?>(ProfileById);
        }

        public Task<SftpConnectionProfile> UpsertAsync(
            SftpConnectionProfile profile,
            CancellationToken cancellationToken = default)
        {
            LastUpsertedProfile = profile;
            LastCancellationToken = cancellationToken;
            return Task.FromResult(profile);
        }

        public Task<bool> HasAnyEncryptedSecretsAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult(false);
        }
    }
}
