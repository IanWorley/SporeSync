using Microsoft.AspNetCore.Mvc;
using Npgsql;
using SftpSync.Web.DTO;

namespace SftpSync.Web.Controllers;

[ApiController]
[Route("api/status")]
public sealed class StatusController : ControllerBase
{
    private readonly NpgsqlDataSource _dataSource;
    private readonly IWebHostEnvironment _environment;

    public StatusController(NpgsqlDataSource dataSource, IWebHostEnvironment environment)
    {
        _dataSource = dataSource;
        _environment = environment;
    }

    [HttpGet]
    public async Task<ActionResult<StatusResponse>> GetStatus(CancellationToken cancellationToken)
    {
        var databaseAvailable = await CanConnectToDatabaseAsync(cancellationToken);

        return Ok(new StatusResponse(
            databaseAvailable ? "ok" : "degraded",
            _environment.EnvironmentName,
            DateTimeOffset.UtcNow,
            databaseAvailable));
    }

    private async Task<bool> CanConnectToDatabaseAsync(CancellationToken cancellationToken)
    {
        try
        {
            await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
            await using var command = new NpgsqlCommand("SELECT 1;", connection);
            await command.ExecuteScalarAsync(cancellationToken);
            return true;
        }
        catch (NpgsqlException)
        {
            return false;
        }
        catch (TimeoutException)
        {
            return false;
        }
    }
}
