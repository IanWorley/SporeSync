using SftpSync.Domain.Model;
using SftpSync.Infrastructure.Repository;

namespace SftpSync.Business.Tests;

public sealed class RepositoryIntegrationTests : IClassFixture<RepositoryTestcontainerFixture>
{
    private readonly RepositoryTestcontainerFixture _fixture;

    public RepositoryIntegrationTests(RepositoryTestcontainerFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task SystemPropertyRepository_UpsertAndGetByName_RoundTripsThroughPostgres()
    {
        var repository = new SystemPropertyRepository(_fixture.DataSource);
        var propertyName = $"sync:test:{Guid.NewGuid()}";

        var upserted = await repository.UpsertAsync(propertyName, "enabled");
        var fetched = await repository.GetByNameAsync(propertyName);

        Assert.NotNull(fetched);
        Assert.Equal(upserted.Id, fetched.Id);
        Assert.Equal(propertyName, fetched.PropertyName);
        Assert.Equal("enabled", fetched.PropertyValue);
    }

    [Fact]
    public async Task SftpConnectionProfileRepository_UpsertAndGetById_RoundTripsThroughPostgres()
    {
        var repository = new SftpConnectionProfileRepository(_fixture.DataSource);
        var profile = new SftpConnectionProfile
        {
            Id = Guid.NewGuid(),
            Name = $"profile-{Guid.NewGuid():N}",
            Host = "sftp.example.com",
            Port = 22,
            Username = "sync-user",
            EncryptedPassword = "encrypted-password",
            IsDefault = false
        };

        var upserted = await repository.UpsertAsync(profile);
        var fetched = await repository.GetByIdAsync(profile.Id);

        Assert.NotNull(fetched);
        Assert.Equal(upserted.Id, fetched.Id);
        Assert.Equal(profile.Name, fetched.Name);
        Assert.Equal(profile.Host, fetched.Host);
        Assert.Equal(profile.Username, fetched.Username);
        Assert.Equal(profile.EncryptedPassword, fetched.EncryptedPassword);
        Assert.False(fetched.IsDefault);
    }

    [Fact]
    public async Task SftpSyncJobRepository_UpsertAndGetById_RoundTripsThroughPostgres()
    {
        var profileRepository = new SftpConnectionProfileRepository(_fixture.DataSource);
        var jobRepository = new SftpSyncJobRepository(_fixture.DataSource);

        var profile = await profileRepository.UpsertAsync(new SftpConnectionProfile
        {
            Id = Guid.NewGuid(),
            Name = $"profile-{Guid.NewGuid():N}",
            Host = "sftp.example.com",
            Port = 22,
            Username = "sync-user",
            EncryptedPassword = "encrypted-password",
            IsDefault = false
        });

        var jobId = Guid.NewGuid();
        var upserted = await jobRepository.UpsertAsync(new UpsertSftpSyncJob
        {
            Id = jobId,
            ConnectionProfileId = profile.Id,
            Name = $"job-{Guid.NewGuid():N}",
            SourcePath = "/incoming",
            DestinationPath = "/local/incoming",
            PollingIntervalSeconds = 120,
            IsEnabled = true
        });

        var fetched = await jobRepository.GetByIdAsync(jobId);

        Assert.NotNull(fetched);
        Assert.Equal(upserted.Id, fetched.Id);
        Assert.Equal(profile.Id, fetched.ConnectionProfileId);
        Assert.Equal(upserted.Name, fetched.Name);
        Assert.Equal("/incoming", fetched.SourcePath);
        Assert.Equal("/local/incoming", fetched.DestinationPath);
        Assert.Equal(120, fetched.PollingIntervalSeconds);
        Assert.True(fetched.IsEnabled);
    }
}
