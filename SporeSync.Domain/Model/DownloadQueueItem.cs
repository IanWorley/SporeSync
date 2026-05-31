namespace SporeSync.Domain.Model;

public sealed class DownloadQueueItem
{
    public required Guid Id { get; init; }

    public required Guid JobId { get; init; }

    public Guid? SyncRunId { get; init; }

    public required string RemotePath { get; init; }

    public required string DestinationPath { get; init; }

    public required long FileSizeBytes { get; init; }

    public DateTimeOffset? RemoteModifiedAt { get; init; }

    public required string Status { get; init; }

    public required long BytesDownloaded { get; init; }

    public decimal? CurrentBytesPerSecond { get; init; }

    public required int RetryCount { get; init; }

    public string? HandledReason { get; init; }

    public string? ErrorMessage { get; init; }

    public required DateTimeOffset QueuedAt { get; init; }

    public DateTimeOffset? StartedAt { get; init; }

    public DateTimeOffset? CompletedAt { get; init; }

    public required DateTimeOffset UpdatedAt { get; init; }

    // Phase 3 additions (per plan:339 + Phase 1 locked columns).
    // Safe defaults (false/NULL/0) on all pre-existing flat rows via Phase 1 migration.
    public required bool IsGroup { get; init; }
    public string? GroupRemotePath { get; init; }
    public required int ChildCount { get; init; }
}
