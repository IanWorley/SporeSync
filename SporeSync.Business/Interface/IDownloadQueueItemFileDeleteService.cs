namespace SporeSync.Business.Interface;

public interface IDownloadQueueItemFileDeleteService
{
    Task<DeleteQueueItemFileResult> DeleteLocalAsync(
        Guid runId,
        Guid queueItemId,
        CancellationToken cancellationToken = default);

    Task<DeleteQueueItemFileResult> DeleteRemoteAsync(
        Guid runId,
        Guid queueItemId,
        CancellationToken cancellationToken = default);
}

public sealed record DeleteQueueItemFileResult(
    DeleteQueueItemFileStatus Status,
    Guid QueueItemId,
    string Target,
    string Path,
    bool Existed,
    string? ErrorMessage = null);

public enum DeleteQueueItemFileStatus
{
    Deleted,
    NotFound,
    JobNotFound,
    Failed
}
