using Microsoft.Extensions.Diagnostics.HealthChecks;
using Npgsql;

namespace SporeSync.Web.Health;

public sealed class DatabaseHealthCheck : IHealthCheck
{
    private readonly NpgsqlDataSource _dataSource;

    public DatabaseHealthCheck(NpgsqlDataSource dataSource)
    {
        _dataSource = dataSource;
    }

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
            await using var command = new NpgsqlCommand("SELECT 1;", connection);
            await command.ExecuteScalarAsync(cancellationToken);
            return HealthCheckResult.Healthy("Database is reachable.");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return HealthCheckResult.Unhealthy("Database is unreachable.", ex);
        }
    }
}
