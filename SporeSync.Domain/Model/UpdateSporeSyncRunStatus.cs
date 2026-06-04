namespace SporeSync.Domain.Model;

public sealed class UpdateSporeSyncRunStatus
{
    public required Guid Id { get; init; }

    public required string Status { get; init; }

    public int? TotalFileCount { get; init; }

    public long? TotalBytes { get; init; }

    public int? CompletedFileCount { get; init; }

    public int? SkippedFileCount { get; init; }

    public int? FailedFileCount { get; init; }

    public long? DownloadedBytes { get; init; }

    public decimal? CurrentBytesPerSecond { get; init; }

    public string? ErrorMessage { get; init; }
}
