using System.ComponentModel.DataAnnotations;

namespace SftpSync.Web.DTO;

public sealed record UpsertSftpSyncJobRequest(
    [property: Required]
    Guid ConnectionProfileId,

    [property: Required]
    [property: MaxLength(200)]
    string Name,

    [property: Required]
    [property: MaxLength(1000)]
    string SourcePath,

    [property: Required]
    [property: MaxLength(1000)]
    string DestinationPath,

    [property: Range(30, int.MaxValue)]
    int PollingIntervalSeconds = 120,

    bool IsEnabled = true);
