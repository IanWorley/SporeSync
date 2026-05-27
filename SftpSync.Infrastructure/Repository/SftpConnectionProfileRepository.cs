using Npgsql;
using SftpSync.Domain.Interface;
using SftpSync.Domain.Model;

namespace SftpSync.Infrastructure.Repository;

public sealed class SftpConnectionProfileRepository : ISftpConnectionProfileRepository
{
    private readonly NpgsqlDataSource _dataSource;

    public SftpConnectionProfileRepository(NpgsqlDataSource dataSource)
    {
        _dataSource = dataSource;
    }

    public async Task<IReadOnlyCollection<SftpConnectionProfile>> GetAllAsync(
        CancellationToken cancellationToken = default)
    {
        const string sql = """
            SELECT id,
                   name,
                   host,
                   port,
                   username,
                   encrypted_password,
                   encrypted_private_key,
                   encrypted_private_key_passphrase,
                   is_default
            FROM core.sftp_connection_profiles
            ORDER BY is_default DESC, name;
            """;

        var profiles = new List<SftpConnectionProfile>();

        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        await using var command = new NpgsqlCommand(sql, connection);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        while (await reader.ReadAsync(cancellationToken))
        {
            profiles.Add(ReadProfile(reader));
        }

        return profiles;
    }

    public async Task<SftpConnectionProfile?> GetByIdAsync(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        const string sql = """
            SELECT id,
                   name,
                   host,
                   port,
                   username,
                   encrypted_password,
                   encrypted_private_key,
                   encrypted_private_key_passphrase,
                   is_default
            FROM core.sftp_connection_profiles
            WHERE id = @id;
            """;

        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("id", id);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        return ReadProfile(reader);
    }

    public async Task<SftpConnectionProfile> UpsertAsync(
        SftpConnectionProfile profile,
        CancellationToken cancellationToken = default)
    {
        const string unsetDefaultSql = """
            UPDATE core.sftp_connection_profiles
            SET is_default = false,
                updated_at = now()
            WHERE is_default = true
              AND id <> @id;
            """;

        const string upsertSql = """
            INSERT INTO core.sftp_connection_profiles (
                id,
                name,
                host,
                port,
                username,
                encrypted_password,
                encrypted_private_key,
                encrypted_private_key_passphrase,
                is_default)
            VALUES (
                @id,
                @name,
                @host,
                @port,
                @username,
                @encrypted_password,
                @encrypted_private_key,
                @encrypted_private_key_passphrase,
                @is_default)
            ON CONFLICT (id)
            DO UPDATE SET
                name = EXCLUDED.name,
                host = EXCLUDED.host,
                port = EXCLUDED.port,
                username = EXCLUDED.username,
                encrypted_password = EXCLUDED.encrypted_password,
                encrypted_private_key = EXCLUDED.encrypted_private_key,
                encrypted_private_key_passphrase = EXCLUDED.encrypted_private_key_passphrase,
                is_default = EXCLUDED.is_default,
                updated_at = now()
            RETURNING id,
                      name,
                      host,
                      port,
                      username,
                      encrypted_password,
                      encrypted_private_key,
                      encrypted_private_key_passphrase,
                      is_default;
            """;

        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        if (profile.IsDefault)
        {
            await using var unsetDefaultCommand = new NpgsqlCommand(unsetDefaultSql, connection, transaction);
            unsetDefaultCommand.Parameters.AddWithValue("id", profile.Id);
            await unsetDefaultCommand.ExecuteNonQueryAsync(cancellationToken);
        }

        await using var command = new NpgsqlCommand(upsertSql, connection, transaction);
        command.Parameters.AddWithValue("id", profile.Id);
        command.Parameters.AddWithValue("name", profile.Name);
        command.Parameters.AddWithValue("host", profile.Host);
        command.Parameters.AddWithValue("port", profile.Port);
        command.Parameters.AddWithValue("username", profile.Username);
        command.Parameters.AddWithValue("encrypted_password", (object?)profile.EncryptedPassword ?? DBNull.Value);
        command.Parameters.AddWithValue("encrypted_private_key", (object?)profile.EncryptedPrivateKey ?? DBNull.Value);
        command.Parameters.AddWithValue(
            "encrypted_private_key_passphrase",
            (object?)profile.EncryptedPrivateKeyPassphrase ?? DBNull.Value);
        command.Parameters.AddWithValue("is_default", profile.IsDefault);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            throw new InvalidOperationException("SFTP connection profile upsert did not return a row.");
        }

        var savedProfile = ReadProfile(reader);
        await reader.DisposeAsync();
        await transaction.CommitAsync(cancellationToken);

        return savedProfile;
    }

    private static SftpConnectionProfile ReadProfile(NpgsqlDataReader reader)
    {
        return new SftpConnectionProfile
        {
            Id = reader.GetGuid(0),
            Name = reader.GetString(1),
            Host = reader.GetString(2),
            Port = reader.GetInt32(3),
            Username = reader.GetString(4),
            EncryptedPassword = reader.IsDBNull(5) ? null : reader.GetString(5),
            EncryptedPrivateKey = reader.IsDBNull(6) ? null : reader.GetString(6),
            EncryptedPrivateKeyPassphrase = reader.IsDBNull(7) ? null : reader.GetString(7),
            IsDefault = reader.GetBoolean(8)
        };
    }
}
