using Microsoft.AspNetCore.Mvc;
using Npgsql;
using SftpSync.Business.Interface;
using SftpSync.Web.DTO;

namespace SftpSync.Web.Controllers;

[ApiController]
[Route("api/status")]
public sealed class StatusController : ControllerBase
{
    private readonly NpgsqlDataSource _dataSource;
    private readonly IWebHostEnvironment _environment;
    private readonly IEncryptionKeyProvider _keyProvider;

    public StatusController(
        NpgsqlDataSource dataSource,
        IWebHostEnvironment environment,
        IEncryptionKeyProvider keyProvider)
    {
        _dataSource = dataSource;
        _environment = environment;
        _keyProvider = keyProvider;
    }

    [HttpGet]
    public async Task<ActionResult<StatusResponse>> GetStatus(CancellationToken cancellationToken)
    {
        var databaseAvailable = await CanConnectToDatabaseAsync(cancellationToken);
        var encryptionKeyInitialized = _keyProvider.IsInitialized;

        return Ok(new StatusResponse(
            databaseAvailable && encryptionKeyInitialized ? "ok" : "degraded",
            _environment.EnvironmentName,
            DateTimeOffset.UtcNow,
            databaseAvailable,
            encryptionKeyInitialized,
            _keyProvider.Version));
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
