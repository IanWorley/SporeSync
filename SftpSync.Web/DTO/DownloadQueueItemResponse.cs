namespace SftpSync.Web.DTO;

public sealed record DownloadQueueItemResponse(
    Guid Id,
    Guid JobId,
    Guid? SyncRunId,
    string RemotePath,
    string DestinationPath,
    long FileSizeBytes,
    DateTimeOffset? RemoteModifiedAt,
    string Status,
    long BytesDownloaded,
    decimal? CurrentBytesPerSecond,
    int RetryCount,
    string? HandledReason,
    string? ErrorMessage,
    DateTimeOffset QueuedAt,
    DateTimeOffset? StartedAt,
    DateTimeOffset? CompletedAt,
    DateTimeOffset UpdatedAt,
    // Phase 3 (plan:339). Additive for backward compat on flat data.
    bool IsGroup,
    string? GroupRemotePath,
    int ChildCount);
