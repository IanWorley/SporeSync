using Microsoft.AspNetCore.Mvc;
using SftpSync.Business.Interface;
using SftpSync.Domain.Model;
using SftpSync.Web;
using SftpSync.Web.Controllers;

namespace SftpSync.Business.Tests;

public sealed class SftpSyncJobsControllerRunTests
{
    [Fact]
    public async Task RunNow_ReturnsConflict_WhenActiveRunExists()
    {
        var controller = new SftpSyncJobsController(
            new FakeSftpSyncJobService(),
            new FakeSyncJobRunService
            {
                Result = new SyncJobRunResult { Error = SyncJobRunError.ActiveRunExists }
            });

        var result = await controller.RunNow(Guid.NewGuid(), CancellationToken.None);

        Assert.IsType<ConflictObjectResult>(result.Result);
    }

    private sealed class FakeSftpSyncJobService : ISftpSyncJobService
    {
        public Task<IReadOnlyCollection<SftpSyncJob>> GetConfiguredJobsAsync(CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyCollection<SftpSyncJob>>(Array.Empty<SftpSyncJob>());

        public Task<SftpSyncJob?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
            => Task.FromResult<SftpSyncJob?>(null);

        public Task<SftpSyncJob> UpsertAsync(UpsertSftpSyncJob job, CancellationToken cancellationToken = default)
            => throw new NotImplementedException();
    }

    private sealed class FakeSyncJobRunService : ISyncJobRunService
    {
        public required SyncJobRunResult Result { get; init; }

        public Task<SyncJobRunResult> TriggerManualRunAsync(Guid jobId, CancellationToken cancellationToken = default)
            => Task.FromResult(Result);
    }
}
