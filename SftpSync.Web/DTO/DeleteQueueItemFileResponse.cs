namespace SftpSync.Web.DTO;

public sealed record DeleteQueueItemFileResponse(
    Guid QueueItemId,
    string Target,
    string Path,
    bool Existed);
