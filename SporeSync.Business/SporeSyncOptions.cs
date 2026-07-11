using System.ComponentModel.DataAnnotations;

namespace SporeSync.Business;

public sealed class SporeSyncOptions
{
    public const string SectionName = "SporeSync";

    [Required(AllowEmptyStrings = false)]
    public string DestinationRootPath { get; set; } =
        Path.Combine(AppContext.BaseDirectory, "downloads");

    [Range(1, 86_400)]
    public int SchedulerIntervalSeconds { get; set; } = 10;

    [Range(1, 600_000)]
    public int DownloadPollIntervalMs { get; set; } = 1000;

    [Range(1, 3_600)]
    public int SftpConnectionTimeoutSeconds { get; set; } = 30;

    [Range(1, 86_400)]
    public int SftpOperationTimeoutSeconds { get; set; } = 300;

    /// <summary>
    /// How long terminal (completed/failed/cancelled) sync runs are kept before
    /// being pruned. Zero disables retention pruning entirely.
    /// </summary>
    [Range(0, 3_650)]
    public int RunHistoryRetentionDays { get; set; } = 30;

    [Range(1, 168)]
    public int RetentionSweepIntervalHours { get; set; } = 6;
}
