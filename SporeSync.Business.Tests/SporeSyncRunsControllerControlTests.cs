using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using SporeSync.Business.Interface;
using SporeSync.Domain.Model;
using SporeSync.Web.Controllers;
using SporeSync.Web.DTO;

namespace SporeSync.Business.Tests;

public sealed class SporeSyncRunsControllerControlTests
{
    [Fact]
    public async Task Cancel_ReturnsNotFound_WhenRunDoesNotExist()
    {
        var controller = CreateController(new SyncRunControlResult { Error = SyncRunControlError.NotFound });

        var result = await controller.Cancel(Guid.NewGuid(), CancellationToken.None);

        Assert.IsType<NotFoundResult>(result.Result);
    }

    [Fact]
    public async Task Cancel_ReturnsConflictProblem_WhenRunIsNotActive()
    {
        var controller = CreateController(new SyncRunControlResult { Error = SyncRunControlError.NotActive });

        var result = await controller.Cancel(Guid.NewGuid(), CancellationToken.None);

        var objectResult = Assert.IsType<ObjectResult>(result.Result);
        var problem = Assert.IsType<ProblemDetails>(objectResult.Value);
        Assert.Equal(StatusCodes.Status409Conflict, problem.Status);
    }

    [Fact]
    public async Task Cancel_ReturnsRun_WhenCancelled()
    {
        var run = CreateRun("cancelled");
        var controller = CreateController(new SyncRunControlResult { Run = run });

        var result = await controller.Cancel(run.Id, CancellationToken.None);

        var okResult = Assert.IsType<OkObjectResult>(result.Result);
        var response = Assert.IsType<SporeSyncRunResponse>(okResult.Value);
        Assert.Equal("cancelled", response.Status);
    }

    [Fact]
    public async Task RetryFailed_ReturnsBadRequestProblem_WhenNothingToRetry()
    {
        var controller = CreateController(new SyncRunControlResult { Error = SyncRunControlError.NoFailedItems });

        var result = await controller.RetryFailed(Guid.NewGuid(), CancellationToken.None);

        var objectResult = Assert.IsType<ObjectResult>(result.Result);
        var problem = Assert.IsType<ProblemDetails>(objectResult.Value);
        Assert.Equal(StatusCodes.Status400BadRequest, problem.Status);
    }

    [Fact]
    public async Task RetryFailed_ReturnsCountAndRun_WhenRetried()
    {
        var run = CreateRun("downloading");
        var controller = CreateController(new SyncRunControlResult { Run = run, RetriedCount = 4 });

        var result = await controller.RetryFailed(run.Id, CancellationToken.None);

        var okResult = Assert.IsType<OkObjectResult>(result.Result);
        var response = Assert.IsType<RetryFailedItemsResponse>(okResult.Value);
        Assert.Equal(4, response.RetriedCount);
        Assert.Equal("downloading", response.Run.Status);
    }

    private static SporeSyncRunsController CreateController(SyncRunControlResult controlResult)
    {
        return new SporeSyncRunsController(
            new FakeRunService(),
            new FakeRunControlService { Result = controlResult },
            new FakeQueueItemService(),
            new FakeFileDeleteService(),
            new FakeNotifier());
    }

    private static SporeSyncRun CreateRun(string status)
    {
        return new SporeSyncRun
        {
            Id = Guid.NewGuid(),
            JobId = Guid.NewGuid(),
            JobName = "job",
            Status = status,
            StartedAt = DateTimeOffset.UtcNow,
            TotalFileCount = 1,
            CompletedFileCount = 0,
            SkippedFileCount = 0,
            FailedFileCount = 1,
            TotalBytes = 100,
            DownloadedBytes = 0
        };
    }

    private sealed class FakeRunControlService : ISyncRunControlService
    {
        public required SyncRunControlResult Result { get; init; }

        public Task<SyncRunControlResult> CancelRunAsync(Guid runId, CancellationToken cancellationToken = default)
            => Task.FromResult(Result);

        public Task<SyncRunControlResult> RetryFailedItemsAsync(Guid runId, CancellationToken cancellationToken = default)
            => Task.FromResult(Result);
    }

    private sealed class FakeRunService : ISporeSyncRunService
    {
        public Task<PagedResult<SporeSyncRun>> GetRunsAsync(RunQuery query, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<SporeSyncRun?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
    }

    private sealed class FakeQueueItemService : IDownloadQueueItemService
    {
        public Task<PagedResult<DownloadQueueItem>> GetByRunIdAsync(Guid runId, QueueItemQuery query, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<IReadOnlyList<DownloadQueueItem>> GetLeavesForGroupAsync(Guid runId, string groupRemotePath, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<RetryQueueItemResult> RetryAsync(Guid runId, Guid queueItemId, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
    }

    private sealed class FakeFileDeleteService : IDownloadQueueItemFileDeleteService
    {
        public Task<DeleteQueueItemFileResult> DeleteLocalAsync(Guid runId, Guid queueItemId, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<DeleteQueueItemFileResult> DeleteRemoteAsync(Guid runId, Guid queueItemId, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
    }

    private sealed class FakeNotifier : ISyncDashboardNotifier
    {
        public Task NotifyRunUpdatedAsync(SporeSyncRun run, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task NotifyQueueItemUpdatedAsync(DownloadQueueItem item, CancellationToken cancellationToken = default)
            => Task.CompletedTask;
    }
}
