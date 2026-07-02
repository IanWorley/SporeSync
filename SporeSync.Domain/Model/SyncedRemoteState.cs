namespace SporeSync.Domain.Model;

public sealed class SyncedRemoteState
{
    public required string RemotePath { get; init; }

    public DateTimeOffset? RemoteModifiedAt { get; init; }

    public required long FileSizeBytes { get; init; }

    public required string Status { get; init; }

    public int ChildCount { get; init; }
}
