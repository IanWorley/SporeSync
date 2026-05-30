using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace SftpSync.Infrastructure.Logging;

public sealed record DbCallLogEntry(
    DateTimeOffset Timestamp,
    string Level,
    string Operation,
    long DurationMs,
    string ParamNames,
    string? ExceptionMessage,
    string? SqlText);

/// <summary>
/// Thread-safe ring buffer for recent DB call log entries (for UI polling).
/// </summary>
public sealed class DbCallLogBuffer
{
    private const int MaxEntries = 500;
    private readonly ConcurrentQueue<DbCallLogEntry> _entries = new();

    public void Add(DbCallLogEntry entry)
    {
        _entries.Enqueue(entry);
        while (_entries.Count > MaxEntries && _entries.TryDequeue(out _))
        {
            // Trim to max size
        }
    }

    public IReadOnlyList<DbCallLogEntry> GetRecent(int limit = 200, LogLevel? minLevel = null)
    {
        var snapshot = _entries.ToArray();
        var filtered = minLevel.HasValue
            ? snapshot.Where(e => ParseLevel(e.Level) >= minLevel.Value)
            : snapshot;

        return filtered
            .OrderByDescending(e => e.Timestamp)
            .Take(limit)
            .ToList();
    }

    private static LogLevel ParseLevel(string level) => level.ToLowerInvariant() switch
    {
        "debug" => LogLevel.Debug,
        "info" or "information" => LogLevel.Information,
        "warn" or "warning" => LogLevel.Warning,
        "error" => LogLevel.Error,
        _ => LogLevel.Information
    };
}
