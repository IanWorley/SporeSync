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

    /// <summary>
    /// When set and the new status is non-terminal, renews the run lease for this
    /// many seconds. Terminal statuses always clear the lease.
    /// </summary>
    public int? LeaseSeconds { get; init; }
}
