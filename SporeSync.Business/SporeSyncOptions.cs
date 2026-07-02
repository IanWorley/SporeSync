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
    /// Number of retries allowed for a queue item after its initial download attempt.
    /// Once exhausted the item is dead-lettered as terminal 'failed'.
    /// </summary>
    public int DownloadMaxRetries { get; set; } = 3;

    /// <summary>Base delay for the exponential retry backoff (base * 2^retryCount).</summary>
    public int DownloadRetryBaseDelaySeconds { get; set; } = 30;

    /// <summary>Upper bound for the exponential retry backoff delay.</summary>
    public int DownloadRetryMaxDelaySeconds { get; set; } = 900;

    /// <summary>
    /// A remote file whose modification time is within this window is considered still
    /// being uploaded; its download is deferred without consuming retry budget.
    /// Set to 0 to disable the stability check.
    /// </summary>
    public int RemoteFileStabilityWindowSeconds { get; set; } = 15;

    /// <summary>
    /// How long terminal (completed/failed/cancelled) sync runs are kept before
    /// being pruned. Zero disables retention pruning entirely.
    /// </summary>
    [Range(0, 3_650)]
    public int RunHistoryRetentionDays { get; set; } = 30;

    [Range(1, 168)]
    public int RetentionSweepIntervalHours { get; set; } = 6;
}
