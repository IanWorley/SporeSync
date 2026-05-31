using System.ComponentModel.DataAnnotations;

namespace SporeSync.Web.DTO;

public sealed record UpsertSporeSyncJobRequest(
    [param: Required]
    Guid ConnectionProfileId,

    [param: Required]
    [param: MaxLength(200)]
    string Name,

    [param: Required]
    [param: MaxLength(1000)]
    string SourcePath,

    [param: Required]
    [param: MaxLength(1000)]
    string DestinationPath,

    [param: Range(30, int.MaxValue)]
    int PollingIntervalSeconds = 120,

    bool IsEnabled = true);
