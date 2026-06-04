using Microsoft.Extensions.Logging;
using SporeSync.Infrastructure.Logging;

namespace SporeSync.Business.Tests;

public sealed class DbCallLogBufferTests
{
    [Theory]
    [InlineData(LogLevel.Debug, "debug", "info", "warning", "error")]
    [InlineData(LogLevel.Information, "info", "warning", "error")]
    [InlineData(LogLevel.Warning, "warning", "error")]
    [InlineData(LogLevel.Error, "error")]
    public void GetRecent_FiltersByStandardMinimumSeverity(LogLevel minLevel, params string[] expectedLevels)
    {
        var buffer = new DbCallLogBuffer();
        AddEntry(buffer, "debug");
        AddEntry(buffer, "info");
        AddEntry(buffer, "warning");
        AddEntry(buffer, "error");

        var entries = buffer.GetRecent(10, minLevel);

        Assert.Equal(expectedLevels.OrderBy(level => level), entries.Select(e => e.Level).OrderBy(level => level));
    }

    private static void AddEntry(DbCallLogBuffer buffer, string level)
    {
        buffer.Add(new DbCallLogEntry(
            DateTimeOffset.UtcNow,
            level,
            $"op-{level}",
            1,
            string.Empty,
            null,
            null));
    }
}
