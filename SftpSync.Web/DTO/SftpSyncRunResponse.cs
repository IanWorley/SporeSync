namespace SftpSync.Web.DTO;

public sealed record SftpSyncRunResponse(
    Guid Id,
    Guid JobId,
    string JobName,
    string Status,
    DateTimeOffset StartedAt,
    DateTimeOffset? CompletedAt,
    int TotalFileCount,
    int CompletedFileCount,
    int SkippedFileCount,
    int FailedFileCount,
    long TotalBytes,
    long DownloadedBytes,
    decimal? CurrentBytesPerSecond,
    string? ErrorMessage);
