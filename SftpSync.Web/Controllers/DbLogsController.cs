using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using SftpSync.Business.Constants;
using SftpSync.Infrastructure.Logging;

namespace SftpSync.Web.Controllers;

[ApiController]
[Route("api/system/db-logs")]
public sealed class DbLogsController : ControllerBase
{
    private readonly DbCallLogBuffer _buffer;
    private readonly DbLoggingConfiguration _config;
    private readonly ILogger<DbLogsController> _logger;

    public DbLogsController(
        DbCallLogBuffer buffer,
        DbLoggingConfiguration config,
        ILogger<DbLogsController> logger)
    {
        _buffer = buffer;
        _config = config;
        _logger = logger;
    }

    [HttpGet]
    public IActionResult GetRecent(
        [FromQuery] string? minLevel = null,
        [FromQuery] int limit = 100)
    {
        var level = ParseLevel(minLevel);
        var entries = _buffer.GetRecent(Math.Clamp(limit, 1, 500), level);

        _logger.LogDebug("Returning {Count} DB log entries (minLevel={MinLevel})", entries.Count, minLevel);
        return Ok(new { items = entries, currentLevel = _config.CurrentLevel.ToString().ToLowerInvariant() });
    }

    private static LogLevel? ParseLevel(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        return value.Trim().ToLowerInvariant() switch
        {
            LogLevelConstants.Debug => LogLevel.Debug,
            LogLevelConstants.Info or LogLevelConstants.Information => LogLevel.Information,
            LogLevelConstants.Warn or LogLevelConstants.Warning => LogLevel.Warning,
            LogLevelConstants.Error => LogLevel.Error,
            _ => null
        };
    }
}
