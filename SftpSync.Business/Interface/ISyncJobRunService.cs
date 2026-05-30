using SftpSync.Domain.Model;

namespace SftpSync.Business.Interface;

public enum SyncJobRunError
{
    NotFound,
    Disabled,
    ActiveRunExists
}

public sealed class SyncJobRunResult
{
    public SftpSyncRun? Run { get; init; }

    public SyncJobRunError? Error { get; init; }
}

public interface ISyncJobRunService
{
    Task<SyncJobRunResult> TriggerManualRunAsync(
        Guid jobId,
        CancellationToken cancellationToken = default);
}
