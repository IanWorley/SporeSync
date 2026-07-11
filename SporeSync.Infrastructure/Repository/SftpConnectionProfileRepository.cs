using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using NpgsqlTypes;
using SporeSync.Domain.Interface;
using SporeSync.Domain.Model;
using SporeSync.Infrastructure.Logging;

namespace SporeSync.Infrastructure.Repository;

public sealed class SftpConnectionProfileRepository : ISftpConnectionProfileRepository
{
    private const string OpGetAllProfiles = "GetAllProfiles";
    private const string OpGetProfileById = "GetProfileById";
    private const string OpUpsertProfile = "UpsertProfile";
    private const string OpHasAnyEncryptedSecrets = "HasAnyEncryptedSecrets";
    private const string OpDeleteProfile = "DeleteProfile";

    private readonly NpgsqlDataSource _dataSource;
    private readonly ILogger<SftpConnectionProfileRepository> _logger;

    public SftpConnectionProfileRepository(
        NpgsqlDataSource dataSource,
        ILogger<SftpConnectionProfileRepository>? logger = null)
    {
        _dataSource = dataSource;
        _logger = logger ?? NullLogger<SftpConnectionProfileRepository>.Instance;
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
                   host_key_fingerprint_sha256,
                   is_default
            FROM core.get_sftp_connection_profiles();
            """;

        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        await using var command = new NpgsqlCommand(sql, connection);

        var profiles = await DbCommandLogger.ExecuteReaderAsync(_logger, command, OpGetAllProfiles,
            async reader =>
            {
                var profiles = new List<SftpConnectionProfile>();
                while (await reader.ReadAsync(cancellationToken))
                {
                    profiles.Add(ReadProfile(reader));
                }
                return profiles;
            }, cancellationToken);
        await LoadTrustedHostKeysAsync(connection, profiles, cancellationToken);
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
                   host_key_fingerprint_sha256,
                   is_default
            FROM core.get_sftp_connection_profile(@id);
            """;

        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("id", id);

        var profile = await DbCommandLogger.ExecuteReaderAsync(_logger, command, OpGetProfileById,
            async reader =>
            {
                if (!await reader.ReadAsync(cancellationToken))
                {
                    return null;
                }
                return ReadProfile(reader);
            }, cancellationToken);
        if (profile is not null)
        {
            await LoadTrustedHostKeysAsync(connection, [profile], cancellationToken);
        }
        return profile;
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
                   host_key_fingerprint_sha256,
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
                @host_key_fingerprint_sha256,
                @is_default);
            """;

        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await using var command = new NpgsqlCommand(sql, connection, transaction);
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
        command.Parameters.AddWithValue(
            "host_key_fingerprint_sha256",
            DBNull.Value);
        command.Parameters.AddWithValue("is_default", profile.IsDefault);

        var saved = await DbCommandLogger.ExecuteReaderAsync(_logger, command, OpUpsertProfile,
            async reader =>
            {
                if (!await reader.ReadAsync(cancellationToken))
                {
                    throw new InvalidOperationException("SFTP connection profile upsert did not return a row.");
                }
                return ReadProfile(reader);
            }, cancellationToken);

        await using var replaceKeys = new NpgsqlCommand("""
            DELETE FROM core.sftp_connection_profile_trusted_host_keys WHERE profile_id = @id;
            INSERT INTO core.sftp_connection_profile_trusted_host_keys (profile_id, fingerprint_sha256)
            SELECT @id, unnest(@fingerprints);
            """, connection, transaction);
        replaceKeys.Parameters.AddWithValue("id", profile.Id);
        replaceKeys.Parameters.AddWithValue(
            "fingerprints",
            NpgsqlDbType.Array | NpgsqlDbType.Varchar,
            profile.TrustedHostKeyFingerprintsSha256.ToArray());
        await replaceKeys.ExecuteNonQueryAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        saved.TrustedHostKeyFingerprintsSha256 = profile.TrustedHostKeyFingerprintsSha256;
        return saved;
    }

    public async Task<bool> HasAnyEncryptedSecretsAsync(CancellationToken cancellationToken = default)
    {
        const string sql = """
            SELECT core.has_any_sftp_connection_profile_encrypted_secrets();
            """;

        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        await using var command = new NpgsqlCommand(sql, connection);

        return (bool)(await DbCommandLogger.ExecuteScalarAsync(_logger, command, OpHasAnyEncryptedSecrets, cancellationToken)
            ?? throw new InvalidOperationException("Encrypted secret existence query did not return a value."));
    }

    public async Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        const string sql = "SELECT core.delete_sftp_connection_profile(@id);";

        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("id", id);

        return await DbCommandLogger.ExecuteScalarAsync<bool>(_logger, command, OpDeleteProfile, cancellationToken);
    }

    public async Task<SafeDeleteSftpConnectionProfileResult> SafeDeleteAsync(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        const string sql = "SELECT core.safe_delete_sftp_connection_profile(@id);";

        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("id", id);

        var result = await DbCommandLogger.ExecuteScalarAsync<string>(_logger, command, OpDeleteProfile, cancellationToken);
        return result switch
        {
            "deleted" => SafeDeleteSftpConnectionProfileResult.Deleted,
            "not_found" => SafeDeleteSftpConnectionProfileResult.NotFound,
            "in_use" => SafeDeleteSftpConnectionProfileResult.InUse,
            _ => throw new InvalidOperationException($"Unexpected safe profile deletion result '{result}'.")
        };
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
            IsDefault = reader.GetBoolean(9)
        };
    }

    private static async Task LoadTrustedHostKeysAsync(
        NpgsqlConnection connection,
        IReadOnlyCollection<SftpConnectionProfile> profiles,
        CancellationToken cancellationToken)
    {
        if (profiles.Count == 0)
        {
            return;
        }

        await using var command = new NpgsqlCommand("""
            SELECT profile_id, fingerprint_sha256
            FROM core.sftp_connection_profile_trusted_host_keys
            WHERE profile_id = ANY(@profile_ids)
            ORDER BY fingerprint_sha256;
            """, connection);
        command.Parameters.AddWithValue("profile_ids", profiles.Select(profile => profile.Id).ToArray());
        var byId = profiles.ToDictionary(profile => profile.Id);
        var keys = profiles.ToDictionary(profile => profile.Id, _ => new List<string>());
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            keys[reader.GetGuid(0)].Add(reader.GetString(1));
        }
        foreach (var (id, fingerprints) in keys)
        {
            byId[id].TrustedHostKeyFingerprintsSha256 = fingerprints;
        }
    }
}
