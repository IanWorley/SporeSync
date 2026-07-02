using Microsoft.Extensions.Options;
using SporeSync.Business;
using SporeSync.Business.Worker;

namespace SporeSync.Business.Tests.Worker;

public sealed class DownloadRetryPolicyTests
{
    [Theory]
    [InlineData(0, 30)]
    [InlineData(1, 60)]
    [InlineData(2, 120)]
    [InlineData(3, 240)]
    public void GetRetryDelay_GrowsExponentiallyFromBaseDelay(int failedAttempts, int expectedSeconds)
    {
        var policy = CreatePolicy(baseDelaySeconds: 30, maxDelaySeconds: 900);

        Assert.Equal(TimeSpan.FromSeconds(expectedSeconds), policy.GetRetryDelay(failedAttempts));
    }

    [Fact]
    public void GetRetryDelay_IsCappedAtMaxDelay()
    {
        var policy = CreatePolicy(baseDelaySeconds: 30, maxDelaySeconds: 900);

        Assert.Equal(TimeSpan.FromSeconds(900), policy.GetRetryDelay(10));
        Assert.Equal(TimeSpan.FromSeconds(900), policy.GetRetryDelay(1000));
    }

    [Fact]
    public void GetRetryDelay_NegativeAttemptCount_UsesBaseDelay()
    {
        var policy = CreatePolicy(baseDelaySeconds: 30, maxDelaySeconds: 900);

        Assert.Equal(TimeSpan.FromSeconds(30), policy.GetRetryDelay(-5));
    }

    [Fact]
    public void MaxRetries_NegativeConfiguration_IsClampedToZero()
    {
        var policy = CreatePolicy(maxRetries: -3);

        Assert.Equal(0, policy.MaxRetries);
    }

    [Fact]
    public void GetRetryDelay_MaxBelowBase_UsesBaseAsCap()
    {
        var policy = CreatePolicy(baseDelaySeconds: 60, maxDelaySeconds: 10);

        Assert.Equal(TimeSpan.FromSeconds(60), policy.GetRetryDelay(5));
    }

    private static DownloadRetryPolicy CreatePolicy(
        int maxRetries = 3,
        int baseDelaySeconds = 30,
        int maxDelaySeconds = 900)
    {
        return new DownloadRetryPolicy(Options.Create(new SporeSyncOptions
        {
            DownloadMaxRetries = maxRetries,
            DownloadRetryBaseDelaySeconds = baseDelaySeconds,
            DownloadRetryMaxDelaySeconds = maxDelaySeconds
        }));
    }
}
