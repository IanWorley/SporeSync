namespace SporeSync.Domain.Model;

public sealed class UpsertSporeSyncJob
{
    public Guid? Id { get; init; }

    public Guid ConnectionProfileId { get; init; }

    public required string Name { get; init; }

    public required string SourcePath { get; init; }

    public required string DestinationPath { get; init; }

    public int PollingIntervalSeconds { get; init; } = 120;

    public bool IsEnabled { get; init; } = true;
}
