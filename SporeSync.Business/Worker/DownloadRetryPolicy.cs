using Microsoft.Extensions.Options;

namespace SporeSync.Business.Worker;

/// <summary>
/// Retry budget and exponential backoff schedule for failed download queue items.
/// </summary>
public sealed class DownloadRetryPolicy
{
    private readonly SporeSyncOptions _options;

    public DownloadRetryPolicy(IOptions<SporeSyncOptions> options)
    {
        _options = options.Value;
    }

    /// <summary>Number of retries allowed after the initial attempt.</summary>
    public int MaxRetries => Math.Max(0, _options.DownloadMaxRetries);

    /// <summary>Delay applied before re-claiming an item that has been deferred for remote stability.</summary>
    public TimeSpan StabilityRecheckDelay =>
        TimeSpan.FromSeconds(Math.Max(1, _options.RemoteFileStabilityWindowSeconds));

    /// <summary>
    /// Exponential backoff delay for the next attempt given how many attempts have already failed:
    /// base * 2^failedAttempts, capped at the configured maximum.
    /// </summary>
    public TimeSpan GetRetryDelay(int failedAttempts)
    {
        var baseDelaySeconds = Math.Max(1, _options.DownloadRetryBaseDelaySeconds);
        var maxDelaySeconds = Math.Max(baseDelaySeconds, _options.DownloadRetryMaxDelaySeconds);

        // Clamp the exponent so the double cannot overflow for pathological retry counts.
        var exponent = Math.Clamp(failedAttempts, 0, 30);
        var delaySeconds = baseDelaySeconds * Math.Pow(2, exponent);

        return TimeSpan.FromSeconds(Math.Min(delaySeconds, maxDelaySeconds));
    }
}
