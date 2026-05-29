using Microsoft.AspNetCore.Mvc;
using SftpSync.Business.Interface;
using SftpSync.Domain.Model;
using SftpSync.Web.Controllers;
using SftpSync.Web.DTO;

namespace SftpSync.Business.Tests;

public sealed class SystemPropertiesControllerTests
{
    [Theory]
    [InlineData("security.encryptionKeyInitialized")]
    [InlineData("security.encryptionKeyVersion")]
    [InlineData("security.encryptionKeyCreatedAtUtc")]
    [InlineData("system.firstRunCompletedAtUtc")]
    [InlineData("unknown.property")]
    public async Task GetByName_ReturnsNotFound_ForReservedOrUnknownPropertyNames(string propertyName)
    {
        var service = new RecordingSystemPropertyService();
        var controller = new SystemPropertiesController(service);

        var result = await controller.GetByName(propertyName, CancellationToken.None);

        Assert.IsType<NotFoundResult>(result.Result);
        Assert.False(service.WasCalled);
    }

    [Fact]
    public async Task Upsert_ReturnsNotFound_ForNonEditablePropertyName()
    {
        var service = new RecordingSystemPropertyService();
        var controller = new SystemPropertiesController(service);

        var result = await controller.Upsert(
            "sync.maxConcurrentDownloads",
            new UpsertSystemPropertyRequest("8"),
            CancellationToken.None);

        Assert.IsType<NotFoundResult>(result.Result);
        Assert.False(service.WasCalled);
    }

    private sealed class RecordingSystemPropertyService : ISystemPropertyService
    {
        public bool WasCalled { get; private set; }

        public Task<SystemProperty?> GetByNameAsync(
            string propertyName,
            CancellationToken cancellationToken = default)
        {
            WasCalled = true;
            return Task.FromResult<SystemProperty?>(null);
        }

        public Task<SystemProperty> UpsertAsync(
            string propertyName,
            string propertyValue,
            CancellationToken cancellationToken = default)
        {
            WasCalled = true;
            return Task.FromResult(new SystemProperty
            {
                Id = Guid.NewGuid(),
                PropertyName = propertyName,
                PropertyValue = propertyValue
            });
        }
    }
}
