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
            FROM core.get_sftp_connection_profiles();
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
            FROM core.get_sftp_connection_profile(@id);
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
            FROM core.upsert_sftp_connection_profile(
                @id,
                @name,
                @host,
                @port,
                @username,
                @encrypted_password,
                @encrypted_private_key,
                @encrypted_private_key_passphrase,
                @is_default);
            """;

        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        await using var command = new NpgsqlCommand(sql, connection);
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

        return ReadProfile(reader);
    }

    public async Task<bool> HasAnyEncryptedSecretsAsync(CancellationToken cancellationToken = default)
    {
        const string sql = """
            SELECT core.has_any_sftp_connection_profile_encrypted_secrets();
            """;

        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        await using var command = new NpgsqlCommand(sql, connection);

        return (bool)(await command.ExecuteScalarAsync(cancellationToken)
            ?? throw new InvalidOperationException("Encrypted secret existence query did not return a value."));
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
