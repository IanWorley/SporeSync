namespace SftpSync.Domain.Model;

public sealed class SftpSyncJob
{
    public Guid Id { get; init; }

    public required string Name { get; init; }

    public required string SourcePath { get; init; }

    public required string DestinationPath { get; init; }

    public bool IsEnabled { get; init; }
}
