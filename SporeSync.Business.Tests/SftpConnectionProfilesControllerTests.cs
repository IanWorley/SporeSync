using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using SporeSync.Business.Interface;
using SporeSync.Business.Sftp;
using SporeSync.Domain.Model;
using SporeSync.Web.Controllers;
using SporeSync.Web.DTO;

namespace SporeSync.Business.Tests;

public sealed class SftpConnectionProfilesControllerTests
{
    [Theory]
    [InlineData("password", SftpAuthenticationMethod.Password, false)]
    [InlineData("privateKey", SftpAuthenticationMethod.PrivateKey, true)]
    public async Task Update_ForwardsExplicitAuthenticationTransition(
        string authenticationMethod,
        SftpAuthenticationMethod expectedMethod,
        bool removePassphrase)
    {
        var service = new FakeProfileService();
        var controller = CreateController(profileService: service);
        var id = Guid.NewGuid();

        await controller.Update(
            id,
            CreateRequest(authenticationMethod, removePassphrase),
            CancellationToken.None);

        Assert.NotNull(service.LastUpsert);
        Assert.Equal(id, service.LastUpsert.Id);
        Assert.Equal(expectedMethod, service.LastUpsert.AuthenticationMethod);
        Assert.Equal("replacement-password", service.LastUpsert.Password);
        Assert.Equal("replacement-key", service.LastUpsert.PrivateKey);
        Assert.Equal("replacement-passphrase", service.LastUpsert.PrivateKeyPassphrase);
        Assert.Equal(removePassphrase, service.LastUpsert.RemovePrivateKeyPassphrase);
    }

    [Fact]
    public async Task Test_ReturnsNotFound_WhenProfileDoesNotExist()
    {
        var controller = CreateController(
            testResult: new SftpConnectionTestResult { ProfileFound = false });

        var result = await controller.Test(TestRequest(), CancellationToken.None);

        Assert.IsType<NotFoundResult>(result.Result);
    }

    [Fact]
    public async Task Test_ReturnsValidationProblem_WhenAuthenticationMethodIsInvalid()
    {
        var controller = CreateController();

        var result = await controller.Test(TestRequest(authenticationMethod: "unsupported"), CancellationToken.None);

        var objectResult = Assert.IsType<ObjectResult>(result.Result);
        var problem = Assert.IsType<ValidationProblemDetails>(objectResult.Value);
        Assert.Equal(
            "Authentication method must be either 'password' or 'privateKey'.",
            problem.Errors[string.Empty].Single());
    }

    [Fact]
    public async Task Test_ReturnsSuccessResponse_WhenConnectionSucceeds()
    {
        var controller = CreateController(
            testResult: new SftpConnectionTestResult
            {
                ProfileFound = true,
                Success = true,
                DurationMs = 42
            });

        var result = await controller.Test(TestRequest(), CancellationToken.None);

        var okResult = Assert.IsType<OkObjectResult>(result.Result);
        var response = Assert.IsType<SftpConnectionTestResponse>(okResult.Value);
        Assert.True(response.Success);
        Assert.Equal(42, response.DurationMs);
    }

    [Fact]
    public async Task Test_ReturnsFailureMessage_WhenConnectionFails()
    {
        var controller = CreateController(
            testResult: new SftpConnectionTestResult
            {
                ProfileFound = true,
                Success = false,
                FailureType = "authentication",
                Message = "Authentication failed. Check the username and credentials."
            });

        var result = await controller.Test(TestRequest(), CancellationToken.None);

        var okResult = Assert.IsType<OkObjectResult>(result.Result);
        var response = Assert.IsType<SftpConnectionTestResponse>(okResult.Value);
        Assert.False(response.Success);
        Assert.Equal("authentication", response.FailureType);
        Assert.Equal("Authentication failed. Check the username and credentials.", response.Message);
    }

    [Fact]
    public async Task Delete_ReturnsConflictProblem_WhenProfileInUse()
    {
        var controller = CreateController(deleteStatus: DeleteSftpConnectionProfileStatus.InUse);

        var result = await controller.Delete(Guid.NewGuid(), CancellationToken.None);

        var objectResult = Assert.IsType<ObjectResult>(result);
        var problem = Assert.IsType<ProblemDetails>(objectResult.Value);
        Assert.Equal(StatusCodes.Status409Conflict, problem.Status);
    }

    [Fact]
    public async Task Delete_ReturnsNoContent_WhenDeleted()
    {
        var controller = CreateController(deleteStatus: DeleteSftpConnectionProfileStatus.Deleted);

        var result = await controller.Delete(Guid.NewGuid(), CancellationToken.None);

        Assert.IsType<NoContentResult>(result);
    }

    [Fact]
    public async Task Delete_ReturnsNotFound_WhenProfileDoesNotExist()
    {
        var controller = CreateController(deleteStatus: DeleteSftpConnectionProfileStatus.NotFound);

        var result = await controller.Delete(Guid.NewGuid(), CancellationToken.None);

        Assert.IsType<NotFoundResult>(result);
    }

    private static SftpConnectionProfilesController CreateController(
        SftpConnectionTestResult? testResult = null,
        DeleteSftpConnectionProfileStatus deleteStatus = DeleteSftpConnectionProfileStatus.Deleted,
        FakeProfileService? profileService = null)
    {
        return new SftpConnectionProfilesController(
            profileService ?? new FakeProfileService { DeleteStatus = deleteStatus },
            new FakeHostKeyScanner(),
            new FakeConnectionTestService
            {
                Result = testResult ?? new SftpConnectionTestResult { ProfileFound = false }
            });
    }

    private static UpsertSftpConnectionProfileRequest CreateRequest(
        string authenticationMethod,
        bool removePassphrase)
    {
        return new UpsertSftpConnectionProfileRequest(
            "profile",
            "sftp.example.com",
            22,
            "sync-user",
            authenticationMethod,
            "replacement-password",
            "replacement-key",
            "replacement-passphrase",
            removePassphrase);
    }

    private static TestSftpConnectionRequest TestRequest(string authenticationMethod = "password") => new(
        null,
        "sftp.example.test",
        22,
        "user",
        authenticationMethod,
        "password",
        null,
        null,
        false,
        null,
        null,
        null);

    private sealed class FakeHostKeyScanner : ISshHostKeyScanner
    {
        public Task<SshHostKeyScanResult> ScanAsync(string host, int port, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
    }

    private sealed class FakeConnectionTestService : ISftpConnectionTestService
    {
        public required SftpConnectionTestResult Result { get; init; }

        public Task<SftpConnectionTestResult> TestAsync(
            SftpConnectionTestRequest request,
            CancellationToken cancellationToken = default)
            => Task.FromResult(Result);
    }

    private sealed class FakeProfileService : ISftpConnectionProfileService
    {
        public DeleteSftpConnectionProfileStatus DeleteStatus { get; init; }

        public UpsertSftpConnectionProfile? LastUpsert { get; private set; }

        public Task<DeleteSftpConnectionProfileStatus> DeleteAsync(Guid id, CancellationToken cancellationToken = default)
            => Task.FromResult(DeleteStatus);

        public Task<IReadOnlyCollection<SftpConnectionProfile>> GetAllAsync(CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<SftpConnectionProfile?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<SftpConnectionProfile> UpsertAsync(UpsertSftpConnectionProfile profile, CancellationToken cancellationToken = default)
        {
            LastUpsert = profile;
            return Task.FromResult(new SftpConnectionProfile
            {
                Id = profile.Id ?? Guid.NewGuid(),
                Name = profile.Name,
                Host = profile.Host,
                Port = profile.Port,
                Username = profile.Username,
                EncryptedPassword = profile.AuthenticationMethod == SftpAuthenticationMethod.Password
                    ? "encrypted-password"
                    : null,
                EncryptedPrivateKey = profile.AuthenticationMethod == SftpAuthenticationMethod.PrivateKey
                    ? "encrypted-key"
                    : null,
                IsDefault = profile.IsDefault
            });
        }
    }
}
