namespace SftpSync.Web.DTO;

public sealed record SftpSyncJobResponse(
    Guid Id,
    string Name,
    string SourcePath,
    string DestinationPath,
    bool IsEnabled);
