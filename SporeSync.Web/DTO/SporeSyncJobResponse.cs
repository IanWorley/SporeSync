namespace SporeSync.Web.DTO;

public sealed record SporeSyncJobResponse(
    Guid Id,
    Guid ConnectionProfileId,
    string Name,
    string SourcePath,
    string DestinationPath,
    int PollingIntervalSeconds,
    bool IsEnabled,
    DateTimeOffset? LastPolledAt);
