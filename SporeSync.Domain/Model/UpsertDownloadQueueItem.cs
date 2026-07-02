namespace SporeSync.Domain.Model;

public sealed class UpsertDownloadQueueItem
{
    public required Guid JobId { get; init; }

    public required Guid SyncRunId { get; init; }

    public required string RemotePath { get; init; }

    public required string DestinationPath { get; init; }

    public required long FileSizeBytes { get; init; }

    public DateTimeOffset? RemoteModifiedAt { get; init; }

    public required bool IsGroup { get; init; }

    public string? GroupRemotePath { get; init; }

    public required int ChildCount { get; init; }

    /// <summary>
    /// When true and the existing row is already completed, the upsert moves the row into the
    /// new sync run but keeps its completed status and downloaded bytes instead of re-queueing it.
    /// Used to carry unchanged group leaves forward so only changed files are re-downloaded.
    /// </summary>
    public bool PreserveCompletedProgress { get; init; }
}
