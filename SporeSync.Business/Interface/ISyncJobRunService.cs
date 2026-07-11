using SporeSync.Domain.Model;

namespace SporeSync.Business.Interface;

public enum SyncJobRunError
{
    NotFound,
    Disabled,
    ActiveRunExists,
    QueueSaturated
}

public sealed class SyncJobRunResult
{
    public SporeSyncRun? Run { get; init; }

    public SyncJobRunError? Error { get; init; }
}

public interface ISyncJobRunService
{
    Task<SyncJobRunResult> TriggerManualRunAsync(
        Guid jobId,
        CancellationToken cancellationToken = default);
}
