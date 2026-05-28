namespace SftpSync.Domain.Model;

public sealed class SftpSyncRun
{
    public required Guid Id { get; init; }

    public required Guid JobId { get; init; }

    public required string JobName { get; init; }

    public required string Status { get; init; }

    public required DateTimeOffset StartedAt { get; init; }

    public DateTimeOffset? CompletedAt { get; init; }

    public required int TotalFileCount { get; init; }

    public required int CompletedFileCount { get; init; }

    public required int SkippedFileCount { get; init; }

    public required int FailedFileCount { get; init; }

    public required long TotalBytes { get; init; }

    public required long DownloadedBytes { get; init; }

    public decimal? CurrentBytesPerSecond { get; init; }

    public string? ErrorMessage { get; init; }
}
