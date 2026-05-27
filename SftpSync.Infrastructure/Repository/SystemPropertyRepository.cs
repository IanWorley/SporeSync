using Npgsql;
using SftpSync.Domain.Interface;
using SftpSync.Domain.Model;
using Visus.Cuid;

namespace SftpSync.Infrastructure.Repository;

public sealed class SystemPropertyRepository : ISystemPropertyRepository
{
    private readonly NpgsqlDataSource _dataSource;

    public SystemPropertyRepository(NpgsqlDataSource dataSource)
    {
        _dataSource = dataSource;
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
        command.Parameters.AddWithValue("id", CreateCuid2());
        command.Parameters.AddWithValue("property_name", propertyName);
        command.Parameters.AddWithValue("property_value", propertyValue);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            throw new InvalidOperationException("System property upsert did not return a row.");
        }

        return ReadSystemProperty(reader);
    }

    private static SystemProperty ReadSystemProperty(NpgsqlDataReader reader)
    {
        return new SystemProperty
        {
            Id = reader.GetString(0),
            PropertyName = reader.GetString(1),
            PropertyValue = reader.GetString(2)
        };
    }

    private static string CreateCuid2()
    {
        return new Cuid2().ToString();
    }
}
