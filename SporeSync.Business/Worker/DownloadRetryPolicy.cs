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
    /// Exponential backoff delay for the next attempt given how many attempts have already failed.
    /// Symmetric jitter is applied to base * 2^failedAttempts, then the result is clamped to the
    /// configured minimum and maximum delay bounds.
    /// </summary>
    public TimeSpan GetRetryDelay(int failedAttempts)
    {
        var minDelaySeconds = Math.Max(1, _options.DownloadRetryBaseDelaySeconds);
        var maxDelaySeconds = Math.Max(minDelaySeconds, _options.DownloadRetryMaxDelaySeconds);

        // Clamp the exponent so the double cannot overflow for pathological retry counts.
        var exponent = Math.Clamp(failedAttempts, 0, 30);
        var exponentialSeconds = Math.Min(minDelaySeconds * Math.Pow(2, exponent), maxDelaySeconds);
        var jitterRatio = Math.Clamp(_options.DownloadRetryJitterRatio, 0, 1);
        var jitterMultiplier = 1 + ((Random.Shared.NextDouble() * 2 - 1) * jitterRatio);
        var delaySeconds = exponentialSeconds * jitterMultiplier;

        return TimeSpan.FromSeconds(Math.Clamp(delaySeconds, minDelaySeconds, maxDelaySeconds));
    }
}
