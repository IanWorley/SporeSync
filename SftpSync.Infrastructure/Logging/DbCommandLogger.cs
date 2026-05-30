using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace SftpSync.Infrastructure.Logging;

public static class DbCommandLogger
{
    private const long SlowQueryThresholdMs = 500;

    private static DbLoggingConfiguration? _config;
    private static DbCallLogBuffer? _buffer;

    public static void Configure(DbLoggingConfiguration config, DbCallLogBuffer buffer)
    {
        _config = config;
        _buffer = buffer;
    }

    public static async Task<T> ExecuteReaderAsync<T>(
        ILogger logger,
        NpgsqlCommand command,
        string operation,
        Func<NpgsqlDataReader, Task<T>> readFunc,
        CancellationToken cancellationToken = default)
    {
        var paramNames = string.Join(", ", command.Parameters.Select(p => p.ParameterName));
        var sqlText = command.CommandText;

        if (_config!.ShouldLog(LogLevel.Debug))
        {
            logger.LogDebug("DB {Operation} executing SQL: {Sql} | Params: [{ParamNames}]",
                operation, sqlText, paramNames);
        }

        var stopwatch = Stopwatch.StartNew();
        try
        {
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            var result = await readFunc(reader);
            stopwatch.Stop();

            LogCompletion(logger, operation, stopwatch.ElapsedMilliseconds, paramNames, sqlText, null);
            return result;
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            LogError(logger, operation, stopwatch.ElapsedMilliseconds, paramNames, sqlText, ex);
            throw;
        }
    }

    public static async Task ExecuteNonQueryAsync(
        ILogger logger,
        NpgsqlCommand command,
        string operation,
        CancellationToken cancellationToken = default)
    {
        var paramNames = string.Join(", ", command.Parameters.Select(p => p.ParameterName));
        var sqlText = command.CommandText;

        if (_config!.ShouldLog(LogLevel.Debug))
        {
            logger.LogDebug("DB {Operation} executing SQL: {Sql} | Params: [{ParamNames}]",
                operation, sqlText, paramNames);
        }

        var stopwatch = Stopwatch.StartNew();
        try
        {
            await command.ExecuteNonQueryAsync(cancellationToken);
            stopwatch.Stop();

            LogCompletion(logger, operation, stopwatch.ElapsedMilliseconds, paramNames, sqlText, null);
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            LogError(logger, operation, stopwatch.ElapsedMilliseconds, paramNames, sqlText, ex);
            throw;
        }
    }

    public static async Task<object?> ExecuteScalarAsync(
        ILogger logger,
        NpgsqlCommand command,
        string operation,
        CancellationToken cancellationToken = default)
    {
        var paramNames = string.Join(", ", command.Parameters.Select(p => p.ParameterName));
        var sqlText = command.CommandText;

        if (_config!.ShouldLog(LogLevel.Debug))
        {
            logger.LogDebug("DB {Operation} executing SQL: {Sql} | Params: [{ParamNames}]",
                operation, sqlText, paramNames);
        }

        var stopwatch = Stopwatch.StartNew();
        try
        {
            var result = await command.ExecuteScalarAsync(cancellationToken);
            stopwatch.Stop();

            LogCompletion(logger, operation, stopwatch.ElapsedMilliseconds, paramNames, sqlText, null);
            return result is DBNull ? null : result;
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            LogError(logger, operation, stopwatch.ElapsedMilliseconds, paramNames, sqlText, ex);
            throw;
        }
    }

    public static async Task<T> ExecuteScalarAsync<T>(
        ILogger logger,
        NpgsqlCommand command,
        string operation,
        CancellationToken cancellationToken = default)
    {
        var result = await ExecuteScalarAsync(logger, command, operation, cancellationToken);

        if (result is null)
        {
            return default!;
        }

        if (result is T typed)
        {
            return typed;
        }

        return (T)Convert.ChangeType(result, typeof(T));
    }

    private static void LogCompletion(
        ILogger logger,
        string operation,
        long durationMs,
        string paramNames,
        string sqlText,
        string? exceptionMessage)
    {
        var level = durationMs > SlowQueryThresholdMs ? LogLevel.Warning : LogLevel.Information;
        var message = durationMs > SlowQueryThresholdMs
            ? "DB {Operation} completed in {DurationMs}ms (SLOW > {Threshold}ms) | Params: [{ParamNames}]"
            : "DB {Operation} completed in {DurationMs}ms | Params: [{ParamNames}]";

        if (_config!.ShouldLog(level))
        {
            if (level == LogLevel.Warning)
            {
                logger.LogWarning(message, operation, durationMs, SlowQueryThresholdMs, paramNames);
            }
            else
            {
                logger.LogInformation(message, operation, durationMs, paramNames);
            }
        }

        _buffer!.Add(new DbCallLogEntry(
            DateTimeOffset.UtcNow,
            level.ToString().ToLowerInvariant(),
            operation,
            durationMs,
            paramNames,
            exceptionMessage,
            sqlText));
    }

    private static void LogError(
        ILogger logger,
        string operation,
        long durationMs,
        string paramNames,
        string sqlText,
        Exception ex)
    {
        if (_config!.ShouldLog(LogLevel.Error))
        {
            logger.LogError(ex, "DB {Operation} failed after {DurationMs}ms | SQL: {Sql} | Params: [{ParamNames}]",
                operation, durationMs, sqlText, paramNames);
        }

        _buffer!.Add(new DbCallLogEntry(
            DateTimeOffset.UtcNow,
            "error",
            operation,
            durationMs,
            paramNames,
            ex.Message,
            sqlText));
    }
}
