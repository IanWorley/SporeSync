using System.Security.Cryptography;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SporeSync.Business.Interface;
using SporeSync.Business.Security;
using SporeSync.Business.Service;
using SporeSync.Business.Sftp;
using SporeSync.Domain.Interface;
using SporeSync.Domain.Model;

namespace SporeSync.Business.Tests.Sftp;

public sealed class SftpConnectionTestIntegrationTests : IClassFixture<SftpTestcontainerFixture>
{
    private readonly SftpTestcontainerFixture _sftp;

    public SftpConnectionTestIntegrationTests(SftpTestcontainerFixture sftp)
    {
        _sftp = sftp;
    }

    [Fact(Timeout = 30_000)]
    public async Task UnsavedConfiguration_ConnectsAndChecksSourcePath()
    {
        var (service, _) = CreateService();

        var result = await TestAsync(service, PasswordRequest(SftpTestcontainerFixture.Password, "/upload"));

        Assert.True(result.Success);
        Assert.Null(result.FailureType);
    }

    [Fact(Timeout = 30_000)]
    public async Task Failures_AreCategorizedWithoutReturningServerExceptionDetails()
    {
        var (service, _) = CreateService();

        var authentication = await TestAsync(service, PasswordRequest("wrong-password"));
        var hostKey = await TestAsync(service, PasswordRequest(
            SftpTestcontainerFixture.Password,
            fingerprint: "SHA256:AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA"));
        var path = await TestAsync(service, PasswordRequest(
            SftpTestcontainerFixture.Password,
            "/upload/does-not-exist"));

        Assert.Equal("authentication", authentication.FailureType);
        Assert.Equal("host_key", hostKey.FailureType);
        Assert.Equal("path", path.FailureType);
        Assert.DoesNotContain("wrong-password", authentication.Message);
    }

    [Fact(Timeout = 30_000)]
    public async Task BlankSecrets_ReuseStoredCredential_AndReplacementIsNotPersisted()
    {
        var (service, repository) = CreateService();
        var protector = repository.Protector;
        var profile = new SftpConnectionProfile
        {
            Id = Guid.NewGuid(),
            Name = "stored",
            Host = _sftp.Host,
            Port = _sftp.Port,
            Username = SftpTestcontainerFixture.Username,
            EncryptedPassword = protector.Protect(SftpTestcontainerFixture.Password),
            IsDefault = false
        };
        repository.Profile = profile;

        var reused = await TestAsync(service, PasswordRequest(null, profileId: profile.Id));
        var replacement = await TestAsync(service, PasswordRequest(
            SftpTestcontainerFixture.Password,
            profileId: profile.Id));

        Assert.True(reused.Success);
        Assert.True(replacement.Success);
        Assert.Same(profile, repository.Profile);
        Assert.Equal(SftpTestcontainerFixture.Password, protector.Unprotect(profile.EncryptedPassword!));
        Assert.Equal(0, repository.UpsertCount);
    }

    [Fact(Timeout = 30_000)]
    public async Task Cancellation_IsPropagated()
    {
        var (service, _) = CreateService();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            service.TestAsync(PasswordRequest(SftpTestcontainerFixture.Password), cancellation.Token));
    }

    private async Task<SftpConnectionTestResult> TestAsync(
        ISftpConnectionTestService service,
        SftpConnectionTestRequest request)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        return await service.TestAsync(request, timeout.Token);
    }

    private SftpConnectionTestRequest PasswordRequest(
        string? password,
        string? sourcePath = null,
        string? fingerprint = null,
        Guid? profileId = null) => new()
    {
        ProfileId = profileId,
        Host = _sftp.Host,
        Port = _sftp.Port,
        Username = SftpTestcontainerFixture.Username,
        Password = password,
        HostKeyFingerprintSha256 = fingerprint,
        SourcePath = sourcePath
    };

    private static (SftpConnectionTestService Service, FakeProfileRepository Repository) CreateService()
    {
        var keyProvider = new EncryptionKeyProvider();
        keyProvider.Initialize(RandomNumberGenerator.GetBytes(32));
        var protector = new SecretProtector(keyProvider);
        var repository = new FakeProfileRepository(protector);
        var options = Options.Create(new SporeSyncOptions
        {
            SftpConnectionTimeoutSeconds = 10,
            SftpOperationTimeoutSeconds = 10
        });
        var factory = new SftpClientFactory(
            repository,
            protector,
            options,
            NullLogger<SftpClientFactory>.Instance);
        return (new SftpConnectionTestService(
            repository,
            factory,
            protector,
            NullLogger<SftpConnectionTestService>.Instance), repository);
    }

    private sealed class FakeProfileRepository(ISecretProtector protector) : ISftpConnectionProfileRepository
    {
        public ISecretProtector Protector { get; } = protector;
        public SftpConnectionProfile? Profile { get; set; }
        public int UpsertCount { get; private set; }

        public Task<SftpConnectionProfile?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
            Task.FromResult(Profile?.Id == id ? Profile : null);

        public Task<SftpConnectionProfile> UpsertAsync(SftpConnectionProfile profile, CancellationToken cancellationToken = default)
        {
            UpsertCount++;
            Profile = profile;
            return Task.FromResult(profile);
        }

        public Task<IReadOnlyCollection<SftpConnectionProfile>> GetAllAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyCollection<SftpConnectionProfile>>(Profile is null ? [] : [Profile]);

        public Task<bool> TryPinHostKeyFingerprintAsync(Guid id, string fingerprintSha256, CancellationToken cancellationToken = default) =>
            Task.FromResult(false);

        public Task<bool> HasAnyEncryptedSecretsAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(Profile?.EncryptedPassword is not null);

        public Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken = default) =>
            Task.FromResult(false);
    }
}
