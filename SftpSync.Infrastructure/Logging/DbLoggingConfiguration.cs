using Microsoft.Extensions.Logging;

namespace SftpSync.Infrastructure.Logging;

/// <summary>
/// Singleton configuration for DB command logging level.
/// Supports runtime updates via the db_log_level system property.
/// </summary>
public sealed class DbLoggingConfiguration
{
    private static readonly LogLevel DefaultLevel = LogLevel.Information;

    private volatile LogLevel _currentLevel = DefaultLevel;

    public LogLevel CurrentLevel => _currentLevel;

    public void SetLevel(string? level)
    {
        _currentLevel = ParseLevel(level);
    }

    public bool ShouldLog(LogLevel level) => level >= _currentLevel;

    private static LogLevel ParseLevel(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return DefaultLevel;
        }

        return value.Trim().ToLowerInvariant() switch
        {
            "debug" => LogLevel.Debug,
            "info" or "information" => LogLevel.Information,
            "warn" or "warning" => LogLevel.Warning,
            "error" => LogLevel.Error,
            _ => DefaultLevel
        };
    }
}
