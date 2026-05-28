using SftpSync.Business.Service;
using SftpSync.Domain.Interface;
using SftpSync.Domain.Model;

namespace SftpSync.Business.Tests;

public sealed class SystemPropertyServiceTests
{
    [Fact]
    public async Task GetByNameAsync_DelegatesToRepository()
    {
        var repository = new RecordingSystemPropertyRepository();
        var service = new SystemPropertyService(repository);
        var cancellationToken = new CancellationTokenSource().Token;

        var result = await service.GetByNameAsync("sync:enabled", cancellationToken);

        Assert.Same(repository.PropertyByName, result);
        Assert.Equal("sync:enabled", repository.LastRequestedPropertyName);
        Assert.Equal(cancellationToken, repository.LastCancellationToken);
    }

    [Fact]
    public async Task UpsertAsync_DelegatesToRepository()
    {
        var repository = new RecordingSystemPropertyRepository();
        var service = new SystemPropertyService(repository);
        var cancellationToken = new CancellationTokenSource().Token;

        var result = await service.UpsertAsync("sync:enabled", "true", cancellationToken);

        Assert.Same(repository.UpsertResult, result);
        Assert.Equal("sync:enabled", repository.LastUpsertedPropertyName);
        Assert.Equal("true", repository.LastUpsertedPropertyValue);
        Assert.Equal(cancellationToken, repository.LastCancellationToken);
    }

    private sealed class RecordingSystemPropertyRepository : ISystemPropertyRepository
    {
        public SystemProperty PropertyByName { get; } = new()
        {
            Id = Guid.NewGuid().ToString(),
            PropertyName = "sync:enabled",
            PropertyValue = "false"
        };

        public SystemProperty UpsertResult { get; } = new()
        {
            Id = Guid.NewGuid().ToString(),
            PropertyName = "sync:enabled",
            PropertyValue = "true"
        };

        public string? LastRequestedPropertyName { get; private set; }

        public string? LastUpsertedPropertyName { get; private set; }

        public string? LastUpsertedPropertyValue { get; private set; }

        public CancellationToken LastCancellationToken { get; private set; }

        public Task<SystemProperty?> GetByNameAsync(
            string propertyName,
            CancellationToken cancellationToken = default)
        {
            LastRequestedPropertyName = propertyName;
            LastCancellationToken = cancellationToken;
            return Task.FromResult<SystemProperty?>(PropertyByName);
        }

        public Task<SystemProperty> UpsertAsync(
            string propertyName,
            string propertyValue,
            CancellationToken cancellationToken = default)
        {
            LastUpsertedPropertyName = propertyName;
            LastUpsertedPropertyValue = propertyValue;
            LastCancellationToken = cancellationToken;
            return Task.FromResult(UpsertResult);
        }

        public Task<SystemProperty> InsertIfMissingAsync(
            string propertyName,
            string propertyValue,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(UpsertResult);
        }
    }
}
