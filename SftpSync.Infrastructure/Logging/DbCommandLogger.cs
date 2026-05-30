using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace SftpSync.Infrastructure.Logging;

internal static class DbCommandLogger
{
    private const long SlowQueryThresholdMs = 500;

    public static async Task<T> ExecuteReaderAsync<T>(
        ILogger logger,
        DbLoggingConfiguration config,
        DbCallLogBuffer buffer,
        NpgsqlCommand command,
        string operation,
        Func<NpgsqlDataReader, Task<T>> readFunc,
        CancellationToken cancellationToken = default)
    {
        var paramNames = string.Join(", ", command.Parameters.Select(p => p.ParameterName));
        var sqlText = command.CommandText;

        if (config.ShouldLog(LogLevel.Debug))
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

            var durationMs = stopwatch.ElapsedMilliseconds;
            LogCompletion(logger, config, buffer, operation, durationMs, paramNames, sqlText, null);
            return result;
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            LogError(logger, config, buffer, operation, stopwatch.ElapsedMilliseconds, paramNames, sqlText, ex);
            throw;
        }
    }

    public static async Task ExecuteNonQueryAsync(
        ILogger logger,
        DbLoggingConfiguration config,
        DbCallLogBuffer buffer,
        NpgsqlCommand command,
        string operation,
        CancellationToken cancellationToken = default)
    {
        var paramNames = string.Join(", ", command.Parameters.Select(p => p.ParameterName));
        var sqlText = command.CommandText;

        if (config.ShouldLog(LogLevel.Debug))
        {
            logger.LogDebug("DB {Operation} executing SQL: {Sql} | Params: [{ParamNames}]",
                operation, sqlText, paramNames);
        }

        var stopwatch = Stopwatch.StartNew();
        try
        {
            await command.ExecuteNonQueryAsync(cancellationToken);
            stopwatch.Stop();

            var durationMs = stopwatch.ElapsedMilliseconds;
            LogCompletion(logger, config, buffer, operation, durationMs, paramNames, sqlText, null);
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            LogError(logger, config, buffer, operation, stopwatch.ElapsedMilliseconds, paramNames, sqlText, ex);
            throw;
        }
    }

    private static void LogCompletion(
        ILogger logger,
        DbLoggingConfiguration config,
        DbCallLogBuffer buffer,
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

        if (config.ShouldLog(level))
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

        buffer.Add(new DbCallLogEntry(
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
        DbLoggingConfiguration config,
        DbCallLogBuffer buffer,
        string operation,
        long durationMs,
        string paramNames,
        string sqlText,
        Exception ex)
    {
        if (config.ShouldLog(LogLevel.Error))
        {
            logger.LogError(ex, "DB {Operation} failed after {DurationMs}ms | Params: [{ParamNames}]",
                operation, durationMs, paramNames);
        }

        buffer.Add(new DbCallLogEntry(
            DateTimeOffset.UtcNow,
            "error",
            operation,
            durationMs,
            paramNames,
            ex.Message,
            sqlText));
    }
}
