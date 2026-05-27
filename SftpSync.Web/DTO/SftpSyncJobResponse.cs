namespace SftpSync.Web.DTO;

public sealed record SftpSyncJobResponse(
    Guid Id,
    Guid ConnectionProfileId,
    string Name,
    string SourcePath,
    string DestinationPath,
    int PollingIntervalSeconds,
    bool IsEnabled,
    DateTimeOffset? LastPolledAt);
