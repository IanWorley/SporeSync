using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using SporeSync.Business.Interface;
using SporeSync.Domain.Model;
using SporeSync.Web;
using SporeSync.Web.Controllers;

namespace SporeSync.Business.Tests;

public sealed class SporeSyncJobsControllerRunTests
{
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

    private sealed class FakeSporeSyncJobService : ISporeSyncJobService
    {
        public Task<IReadOnlyCollection<SporeSyncJob>> GetConfiguredJobsAsync(CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyCollection<SporeSyncJob>>(Array.Empty<SporeSyncJob>());

        public Task<SporeSyncJob?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
            => Task.FromResult<SporeSyncJob?>(null);

        public Task<SporeSyncJob> UpsertAsync(UpsertSporeSyncJob job, CancellationToken cancellationToken = default)
            => throw new NotImplementedException();

        public Task<DeleteSporeSyncJobStatus> DeleteAsync(Guid id, CancellationToken cancellationToken = default)
            => throw new NotImplementedException();
    }

    private sealed class FakeSyncJobRunService : ISyncJobRunService
    {
        public required SyncJobRunResult Result { get; init; }

        public Task<SyncJobRunResult> TriggerManualRunAsync(Guid jobId, CancellationToken cancellationToken = default)
            => Task.FromResult(Result);
    }
}
