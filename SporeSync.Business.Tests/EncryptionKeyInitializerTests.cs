using System.Security.Cryptography;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using SporeSync.Business.Service;
using SporeSync.Domain.Interface;
using SporeSync.Domain.Model;

namespace SporeSync.Business.Tests;

public sealed class EncryptionKeyInitializerTests
{
    [Fact]
    public async Task InitializeAsync_AcceptsValidKeyFileAndCachesKey()
    {
        using var temp = new TempDirectory();
        var key = RandomNumberGenerator.GetBytes(32);
        var keyPath = temp.KeyPath;
        await File.WriteAllTextAsync(keyPath, Convert.ToBase64String(key));
        var keyProvider = new EncryptionKeyProvider();

        await CreateInitializer(keyPath, keyProvider).InitializeAsync();

        Assert.True(keyProvider.IsInitialized);
        Assert.Equal(key, keyProvider.GetKey());
    }

    [Fact]
    public async Task InitializeAsync_MissingKeyOnFirstBootCreatesKeyAndMetadata()
    {
        using var temp = new TempDirectory();
        var properties = new RecordingSystemPropertyRepository();
        var keyProvider = new EncryptionKeyProvider();

        await CreateInitializer(temp.KeyPath, keyProvider, properties).InitializeAsync();

        Assert.True(File.Exists(temp.KeyPath));
        Assert.True(keyProvider.IsInitialized);
        Assert.Equal(32, keyProvider.GetKey().Length);
        Assert.Equal("true", properties.Values[EncryptionKeyInitializer.InitializedPropertyName]);
        Assert.Equal("v1", properties.Values[EncryptionKeyInitializer.VersionPropertyName]);
        Assert.True(properties.Values.ContainsKey(EncryptionKeyInitializer.CreatedAtPropertyName));
        Assert.True(properties.Values.ContainsKey(EncryptionKeyInitializer.FirstRunCompletedAtPropertyName));
    }

    [Fact]
    public async Task InitializeAsync_MissingKeyAfterFirstRunSignoffFails()
    {
        using var temp = new TempDirectory();
        var properties = new RecordingSystemPropertyRepository();
        properties.Values[EncryptionKeyInitializer.FirstRunCompletedAtPropertyName] = DateTimeOffset.UtcNow.ToString("O");

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            CreateInitializer(temp.KeyPath, properties: properties).InitializeAsync());

        Assert.Equal(
            "Encryption key file is missing and this system has already completed first-run initialization. Restore the key file or intentionally reset the deployment.",
            exception.Message);
        Assert.False(File.Exists(temp.KeyPath));
    }

    [Fact]
    public async Task InitializeAsync_MissingKeyWithEncryptedProfilesFails()
    {
        using var temp = new TempDirectory();

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            CreateInitializer(
                temp.KeyPath,
                profiles: new RecordingSftpConnectionProfileRepository { HasEncryptedSecrets = true }).InitializeAsync());

        Assert.Equal(
            "Encryption key file is missing and encrypted SFTP profiles already exist. Restore the key file or delete/recreate the profiles.",
            exception.Message);
        Assert.False(File.Exists(temp.KeyPath));
    }

    [Theory]
    [InlineData("not-base64")]
    [InlineData("dGlueQ==")]
    public async Task InitializeAsync_InvalidKeyFileFailsAndDoesNotOverwrite(string keyFileContents)
    {
        using var temp = new TempDirectory();
        await File.WriteAllTextAsync(temp.KeyPath, keyFileContents);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            CreateInitializer(temp.KeyPath).InitializeAsync());

        Assert.Equal("Encryption key file is invalid. It must contain base64 text for exactly 32 bytes.", exception.Message);
        Assert.Equal(keyFileContents, await File.ReadAllTextAsync(temp.KeyPath));
    }

    [Fact]
    public async Task InitializeAsync_PreProvisionedKeyCreatesFirstRunSignoffOnlyOnce()
    {
        using var temp = new TempDirectory();
        await File.WriteAllTextAsync(temp.KeyPath, Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)));
        var properties = new RecordingSystemPropertyRepository();

        await CreateInitializer(temp.KeyPath, properties: properties).InitializeAsync();
        var firstSignoff = properties.Values[EncryptionKeyInitializer.FirstRunCompletedAtPropertyName];

        await CreateInitializer(temp.KeyPath, properties: properties).InitializeAsync();

        Assert.Equal(firstSignoff, properties.Values[EncryptionKeyInitializer.FirstRunCompletedAtPropertyName]);
    }

    private static EncryptionKeyInitializer CreateInitializer(
        string keyPath,
        EncryptionKeyProvider? keyProvider = null,
        RecordingSystemPropertyRepository? properties = null,
        RecordingSftpConnectionProfileRepository? profiles = null)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Security:EncryptionKeyPath"] = keyPath
            })
            .Build();

        return new EncryptionKeyInitializer(
            configuration,
            keyProvider ?? new EncryptionKeyProvider(),
            profiles ?? new RecordingSftpConnectionProfileRepository(),
            properties ?? new RecordingSystemPropertyRepository(),
            NullLogger<EncryptionKeyInitializer>.Instance);
    }

    private sealed class TempDirectory : IDisposable
    {
        public TempDirectory()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"sporesync-{Guid.NewGuid():N}");
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public string KeyPath => System.IO.Path.Combine(Path, "encryption.key");

        public void Dispose()
        {
            Directory.Delete(Path, recursive: true);
        }
    }

    private sealed class RecordingSystemPropertyRepository : ISystemPropertyRepository
    {
        public Dictionary<string, string> Values { get; } = [];

        public Task<SystemProperty?> GetByNameAsync(
            string propertyName,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(Values.TryGetValue(propertyName, out var value)
                ? new SystemProperty { Id = Guid.NewGuid(), PropertyName = propertyName, PropertyValue = value }
                : null);
        }

        public Task<SystemProperty> UpsertAsync(
            string propertyName,
            string propertyValue,
            CancellationToken cancellationToken = default)
        {
            Values[propertyName] = propertyValue;
            return Task.FromResult(CreateProperty(propertyName, propertyValue));
        }

        public Task<SystemProperty> InsertIfMissingAsync(
            string propertyName,
            string propertyValue,
            CancellationToken cancellationToken = default)
        {
            if (!Values.ContainsKey(propertyName))
            {
                Values[propertyName] = propertyValue;
            }

            return Task.FromResult(CreateProperty(propertyName, Values[propertyName]));
        }

        private static SystemProperty CreateProperty(string propertyName, string propertyValue)
        {
            return new SystemProperty
            {
                Id = Guid.NewGuid(),
                PropertyName = propertyName,
                PropertyValue = propertyValue
            };
        }
    }

    private sealed class RecordingSftpConnectionProfileRepository : ISftpConnectionProfileRepository
    {
        public bool HasEncryptedSecrets { get; init; }

        public Task<IReadOnlyCollection<SftpConnectionProfile>> GetAllAsync(CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public Task<SftpConnectionProfile?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public Task<SftpConnectionProfile> UpsertAsync(
            SftpConnectionProfile profile,
            CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public Task<bool> TryPinHostKeyFingerprintAsync(
            Guid id,
            string fingerprintSha256,
            CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public Task<bool> HasAnyEncryptedSecretsAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult(HasEncryptedSecrets);
        }

        public Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public Task<SafeDeleteSftpConnectionProfileResult> SafeDeleteAsync(
            Guid id,
            CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }
    }
}
