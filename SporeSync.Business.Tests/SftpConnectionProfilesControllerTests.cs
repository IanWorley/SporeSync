using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using SporeSync.Business.Interface;
using SporeSync.Domain.Model;
using SporeSync.Web.Controllers;
using SporeSync.Web.DTO;

namespace SporeSync.Business.Tests;

public sealed class SftpConnectionProfilesControllerTests
{
    [Fact]
    public async Task Test_ReturnsNotFound_WhenProfileDoesNotExist()
    {
        var controller = CreateController(
            testResult: new SftpConnectionTestResult { ProfileFound = false });

        var result = await controller.Test(Guid.NewGuid(), CancellationToken.None);

        Assert.IsType<NotFoundResult>(result.Result);
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

        var result = await controller.Test(Guid.NewGuid(), CancellationToken.None);

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
                ErrorMessage = "Permission denied (password)."
            });

        var result = await controller.Test(Guid.NewGuid(), CancellationToken.None);

        var okResult = Assert.IsType<OkObjectResult>(result.Result);
        var response = Assert.IsType<SftpConnectionTestResponse>(okResult.Value);
        Assert.False(response.Success);
        Assert.Equal("Permission denied (password).", response.Message);
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

    private static SftpConnectionProfilesController CreateController(
        SftpConnectionTestResult? testResult = null,
        DeleteSftpConnectionProfileStatus deleteStatus = DeleteSftpConnectionProfileStatus.Deleted)
    {
        return new SftpConnectionProfilesController(
            new FakeProfileService { DeleteStatus = deleteStatus },
            new FakeConnectionTestService
            {
                Result = testResult ?? new SftpConnectionTestResult { ProfileFound = false }
            });
    }

    private sealed class FakeConnectionTestService : ISftpConnectionTestService
    {
        public required SftpConnectionTestResult Result { get; init; }

        public Task<SftpConnectionTestResult> TestAsync(Guid profileId, CancellationToken cancellationToken = default)
            => Task.FromResult(Result);
    }

    private sealed class FakeProfileService : ISftpConnectionProfileService
    {
        public DeleteSftpConnectionProfileStatus DeleteStatus { get; init; }

        public Task<DeleteSftpConnectionProfileStatus> DeleteAsync(Guid id, CancellationToken cancellationToken = default)
            => Task.FromResult(DeleteStatus);

        public Task<IReadOnlyCollection<SftpConnectionProfile>> GetAllAsync(CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<SftpConnectionProfile?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<SftpConnectionProfile> UpsertAsync(UpsertSftpConnectionProfile profile, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
    }
}
