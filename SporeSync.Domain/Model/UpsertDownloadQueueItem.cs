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
}
