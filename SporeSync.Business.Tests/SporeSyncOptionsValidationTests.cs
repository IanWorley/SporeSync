using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace SporeSync.Business.Tests;

public sealed class SporeSyncOptionsValidationTests
{
    [Fact]
    public void ValidConfiguration_ResolvesOptions()
    {
        var options = ResolveOptions(new Dictionary<string, string?>
        {
            ["SporeSync:DestinationRootPath"] = Path.Combine(Path.GetTempPath(), "sporesync-downloads"),
            ["SporeSync:SchedulerIntervalSeconds"] = "10",
            ["SporeSync:RunScanLeaseSeconds"] = "1800",
            ["SporeSync:RunHistoryRetentionDays"] = "30"
        });

        Assert.Equal(10, options.Value.SchedulerIntervalSeconds);
        Assert.Equal(1800, options.Value.RunScanLeaseSeconds);
        Assert.Equal(30, options.Value.RunHistoryRetentionDays);
    }

    [Theory]
    [InlineData("SporeSync:SchedulerIntervalSeconds", "0")]
    [InlineData("SporeSync:DownloadPollIntervalMs", "-1")]
    [InlineData("SporeSync:SftpConnectionTimeoutSeconds", "0")]
    [InlineData("SporeSync:SftpOperationTimeoutSeconds", "-5")]
    [InlineData("SporeSync:DownloadLeaseSeconds", "0")]
    [InlineData("SporeSync:RunScanLeaseSeconds", "0")]
    [InlineData("SporeSync:RecoverySweepIntervalSeconds", "0")]
    [InlineData("SporeSync:RunHistoryRetentionDays", "-1")]
    [InlineData("SporeSync:RetentionSweepIntervalHours", "0")]
    public void OutOfRangeValue_FailsValidation(string key, string value)
    {
        var options = ResolveOptions(new Dictionary<string, string?> { [key] = value });

        Assert.Throws<OptionsValidationException>(() => options.Value);
    }

    [Fact]
    public void RelativeDestinationRootPath_FailsValidation()
    {
        var options = ResolveOptions(new Dictionary<string, string?>
        {
            ["SporeSync:DestinationRootPath"] = "relative/downloads"
        });

        Assert.Throws<OptionsValidationException>(() => options.Value);
    }

    [Fact]
    public void RetentionDisabledWithZeroDays_PassesValidation()
    {
        var options = ResolveOptions(new Dictionary<string, string?>
        {
            ["SporeSync:RunHistoryRetentionDays"] = "0"
        });

        Assert.Equal(0, options.Value.RunHistoryRetentionDays);
    }

    private static IOptions<SporeSyncOptions> ResolveOptions(Dictionary<string, string?> settings)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(settings)
            .Build();

        var services = new ServiceCollection();
        services.AddOptions<SporeSyncOptions>()
            .Bind(configuration.GetSection(SporeSyncOptions.SectionName))
            .ValidateDataAnnotations()
            .Validate(
                options => Path.IsPathFullyQualified(options.DestinationRootPath),
                "DestinationRootPath must be an absolute path.");

        return services.BuildServiceProvider().GetRequiredService<IOptions<SporeSyncOptions>>();
    }
}
