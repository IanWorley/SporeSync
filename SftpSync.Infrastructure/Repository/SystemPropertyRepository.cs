using Microsoft.Extensions.Logging;
using Npgsql;
using SftpSync.Domain.Interface;
using SftpSync.Domain.Model;
using SftpSync.Infrastructure.Logging;

namespace SftpSync.Infrastructure.Repository;

public sealed class SystemPropertyRepository : ISystemPropertyRepository
{
    private readonly NpgsqlDataSource _dataSource;
    private readonly ILogger<SystemPropertyRepository> _logger;

    public SystemPropertyRepository(NpgsqlDataSource dataSource, ILogger<SystemPropertyRepository> logger)
    {
        _dataSource = dataSource;
        _logger = logger;
    }

    public async Task<SystemProperty?> GetByNameAsync(
        string propertyName,
        CancellationToken cancellationToken = default)
    {
        const string sql = """
            SELECT id, property_name, property_value
            FROM core.get_system_property(@property_name);
            """;

        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("property_name", propertyName);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        return ReadSystemProperty(reader);
    }

    public async Task<SystemProperty> UpsertAsync(
        string propertyName,
        string propertyValue,
        CancellationToken cancellationToken = default)
    {
        const string sql = """
            SELECT id, property_name, property_value
            FROM core.upsert_system_property(@id, @property_name, @property_value);
            """;

        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("id", Guid.NewGuid());
        command.Parameters.AddWithValue("property_name", propertyName);
        command.Parameters.AddWithValue("property_value", propertyValue);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            throw new InvalidOperationException("System property upsert did not return a row.");
        }

        return ReadSystemProperty(reader);
    }

    public async Task<SystemProperty> InsertIfMissingAsync(
        string propertyName,
        string propertyValue,
        CancellationToken cancellationToken = default)
    {
        const string sql = """
            SELECT id, property_name, property_value
            FROM core.insert_system_property_if_missing(@id, @property_name, @property_value);
            """;

        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("id", Guid.NewGuid());
        command.Parameters.AddWithValue("property_name", propertyName);
        command.Parameters.AddWithValue("property_value", propertyValue);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            throw new InvalidOperationException("System property insert-if-missing did not return a row.");
        }

        return ReadSystemProperty(reader);
    }

    private static SystemProperty ReadSystemProperty(NpgsqlDataReader reader)
    {
        return new SystemProperty
        {
            Id = reader.GetGuid(0),
            PropertyName = reader.GetString(1),
            PropertyValue = reader.GetString(2)
        };
    }
}
