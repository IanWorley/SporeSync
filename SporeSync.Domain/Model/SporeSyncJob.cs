namespace SporeSync.Domain.Model;

public sealed class SporeSyncJob
{
    public Guid Id { get; init; }

    public Guid ConnectionProfileId { get; init; }

    public required string Name { get; init; }

    public required string SourcePath { get; init; }

    public required string DestinationPath { get; init; }

    public int PollingIntervalSeconds { get; init; }

    public bool IsEnabled { get; init; }

    public DateTimeOffset? LastPolledAt { get; init; }
}
