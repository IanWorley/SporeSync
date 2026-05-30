using Microsoft.Extensions.Logging;
using SftpSync.Infrastructure.Logging;

namespace SftpSync.Business.Tests;

public sealed class DbLoggingConfigurationTests
{
    [Theory]
    [InlineData("debug", true, true, true, true)]
    [InlineData("info", false, true, true, true)]
    [InlineData("information", false, true, true, true)]
    [InlineData("warning", false, false, true, true)]
    [InlineData("warn", false, false, true, true)]
    [InlineData("error", false, false, false, true)]
    public void ShouldLog_UsesStandardMinimumSeverity(
        string configuredLevel,
        bool logsDebug,
        bool logsInfo,
        bool logsWarning,
        bool logsError)
    {
        var config = new DbLoggingConfiguration();

        config.SetLevel(configuredLevel);

        Assert.Equal(logsDebug, config.ShouldLog(LogLevel.Debug));
        Assert.Equal(logsInfo, config.ShouldLog(LogLevel.Information));
        Assert.Equal(logsWarning, config.ShouldLog(LogLevel.Warning));
        Assert.Equal(logsError, config.ShouldLog(LogLevel.Error));
    }

    [Fact]
    public void ShouldLog_DefaultsToInformation_ForUnknownLevel()
    {
        var config = new DbLoggingConfiguration();

        config.SetLevel("not-a-level");

        Assert.False(config.ShouldLog(LogLevel.Debug));
        Assert.True(config.ShouldLog(LogLevel.Information));
        Assert.True(config.ShouldLog(LogLevel.Warning));
        Assert.True(config.ShouldLog(LogLevel.Error));
    }
}
