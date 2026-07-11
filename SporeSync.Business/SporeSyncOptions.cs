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

    /// Lease duration stamped on claimed queue items. The worker renews the lease
    /// while a download is in progress; if the process dies, the recovery sweep
    /// requeues the item once the lease expires.
    /// </summary>
    [Range(1, 86_400)]
    public int DownloadLeaseSeconds { get; set; } = 300;

    /// <summary>
    /// Lease duration for runs in the queued/scanning phase. The scanner renews
    /// this lease while a scan is in progress so recovery can distinguish an
    /// active long scan from a crashed one.
    /// </summary>
    [Range(1, 86_400)]
    public int RunScanLeaseSeconds { get; set; } = 1800;

    /// <summary>Maximum number of manual scans waiting for the background worker.</summary>
    [Range(1, 10_000)]
    public int ManualRunQueueCapacity { get; set; } = 32;

    /// <summary>
    /// Interval between periodic recovery sweeps (stale item requeue and orphaned
    /// run reaping).
    /// </summary>
    [Range(1, 86_400)]
    public int RecoverySweepIntervalSeconds { get; set; } = 60;

    /// <summary>
    /// Number of retries allowed for a queue item after its initial download attempt.
    /// Once exhausted the item is dead-lettered as terminal 'failed'.
    /// </summary>
    [Range(0, 100)]
    public int DownloadMaxRetries { get; set; } = 3;

    /// <summary>Base delay for the exponential retry backoff (base * 2^retryCount).</summary>
    [Range(1, 86_400)]
    public int DownloadRetryBaseDelaySeconds { get; set; } = 30;

    /// <summary>Upper bound for the exponential retry backoff delay.</summary>
    [Range(1, 86_400)]
    public int DownloadRetryMaxDelaySeconds { get; set; } = 900;

    /// <summary>Maximum proportional jitter applied above or below each exponential delay.</summary>
    [Range(typeof(double), "0", "1")]
    public double DownloadRetryJitterRatio { get; set; } = 0.2;

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
