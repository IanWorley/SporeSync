using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using SporeSync.Business.Interface;
using SporeSync.Domain.Model;
using SporeSync.Web;
using SporeSync.Web.Controllers;
using SporeSync.Web.DTO;

namespace SporeSync.Business.Tests;

public sealed class SporeSyncJobsControllerRunTests
{
    [Fact]
    public async Task RunNow_ReturnsAcceptedWithQueuedRun()
    {
        var run = new SporeSyncRun
        {
            Id = Guid.NewGuid(), JobId = Guid.NewGuid(), JobName = "manual", Status = "queued",
            StartedAt = DateTimeOffset.UtcNow, TotalFileCount = 0, CompletedFileCount = 0,
            SkippedFileCount = 0, FailedFileCount = 0, TotalBytes = 0, DownloadedBytes = 0
        };
        var controller = new SporeSyncJobsController(
            new FakeSporeSyncJobService(),
            new FakeSyncJobRunService { Result = new SyncJobRunResult { Run = run } });

        var result = await controller.RunNow(run.JobId, CancellationToken.None);

        var accepted = Assert.IsType<AcceptedResult>(result.Result);
        var response = Assert.IsType<SporeSyncRunResponse>(accepted.Value);
        Assert.Equal(run.Id, response.Id);
    }

    [Fact]
    public async Task RunNow_ReturnsConflict_WhenActiveRunExists()
    {
        var controller = new SporeSyncJobsController(
            new FakeSporeSyncJobService(),
            new FakeSyncJobRunService
            {
                Result = new SyncJobRunResult { Error = SyncJobRunError.ActiveRunExists }
            });

        var result = await controller.RunNow(Guid.NewGuid(), CancellationToken.None);

        var objectResult = Assert.IsType<ObjectResult>(result.Result);
        var problem = Assert.IsType<ProblemDetails>(objectResult.Value);
        Assert.Equal(StatusCodes.Status409Conflict, problem.Status);
    }

    [Fact]
    public async Task RunNow_ReturnsTooManyRequests_WhenManualQueueIsFull()
    {
        var controller = new SporeSyncJobsController(
            new FakeSporeSyncJobService(),
            new FakeSyncJobRunService
            {
                Result = new SyncJobRunResult { Error = SyncJobRunError.QueueSaturated }
            });

        var result = await controller.RunNow(Guid.NewGuid(), CancellationToken.None);

        var objectResult = Assert.IsType<ObjectResult>(result.Result);
        Assert.Equal(StatusCodes.Status429TooManyRequests, objectResult.StatusCode);
    }

    [Fact]
    public async Task Delete_ReturnsConflict_WhenActiveRunExists()
    {
        var controller = CreateController(DeleteSporeSyncJobStatus.ActiveRunExists);

        var result = await controller.Delete(Guid.NewGuid(), CancellationToken.None);

        var objectResult = Assert.IsType<ObjectResult>(result);
        var problem = Assert.IsType<ProblemDetails>(objectResult.Value);
        Assert.Equal(StatusCodes.Status409Conflict, problem.Status);
    }

    [Fact]
    public async Task Delete_ReturnsNotFound_WhenJobDoesNotExist()
    {
        var controller = CreateController(DeleteSporeSyncJobStatus.NotFound);

        var result = await controller.Delete(Guid.NewGuid(), CancellationToken.None);

        Assert.IsType<NotFoundResult>(result);
    }

    private static SporeSyncJobsController CreateController(DeleteSporeSyncJobStatus deleteStatus)
    {
        return new SporeSyncJobsController(
            new FakeSporeSyncJobService { DeleteStatus = deleteStatus },
            new FakeSyncJobRunService { Result = new SyncJobRunResult { Error = SyncJobRunError.NotFound } });
    }

    private sealed class FakeSporeSyncJobService : ISporeSyncJobService
    {
        public DeleteSporeSyncJobStatus DeleteStatus { get; init; }

        public Task<IReadOnlyCollection<SporeSyncJob>> GetConfiguredJobsAsync(CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyCollection<SporeSyncJob>>(Array.Empty<SporeSyncJob>());

        public Task<SporeSyncJob?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
            => Task.FromResult<SporeSyncJob?>(null);

        public Task<SporeSyncJob> UpsertAsync(UpsertSporeSyncJob job, CancellationToken cancellationToken = default)
            => throw new NotImplementedException();

        public Task<DeleteSporeSyncJobStatus> DeleteAsync(Guid id, CancellationToken cancellationToken = default)
            => Task.FromResult(DeleteStatus);
    }

    private sealed class FakeSyncJobRunService : ISyncJobRunService
    {
        public required SyncJobRunResult Result { get; init; }

        public Task<SyncJobRunResult> TriggerManualRunAsync(Guid jobId, CancellationToken cancellationToken = default)
            => Task.FromResult(Result);
    }
}
