using Npgsql;
using SftpSync.Domain.Interface;
using SftpSync.Domain.Model;

namespace SftpSync.Infrastructure.Repository;

public sealed class SftpSyncJobRepository : ISftpSyncJobRepository
{
    private readonly NpgsqlDataSource _dataSource;

    public SftpSyncJobRepository(NpgsqlDataSource dataSource)
    {
        _dataSource = dataSource;
    }

    public async Task<IReadOnlyCollection<SftpSyncJob>> GetAllAsync(
        CancellationToken cancellationToken = default)
    {
        const string sql = """
            SELECT id,
                   connection_profile_id,
                   name,
                   source_path,
                   destination_path,
                   polling_interval_seconds,
                   is_enabled,
                   last_polled_at
            FROM core.get_sftp_sync_jobs();
            """;

        var jobs = new List<SftpSyncJob>();

        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        await using var command = new NpgsqlCommand(sql, connection);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        while (await reader.ReadAsync(cancellationToken))
        {
            jobs.Add(ReadSftpSyncJob(reader));
        }

        return jobs;
    }

    public async Task<SftpSyncJob?> GetByIdAsync(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        const string sql = """
            SELECT id,
                   connection_profile_id,
                   name,
                   source_path,
                   destination_path,
                   polling_interval_seconds,
                   is_enabled,
                   last_polled_at
            FROM core.get_sftp_sync_job(@id);
            """;

        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("id", id);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        return ReadSftpSyncJob(reader);
    }

    public async Task<SftpSyncJob> UpsertAsync(
        UpsertSftpSyncJob job,
        CancellationToken cancellationToken = default)
    {
        const string sql = """
            SELECT id,
                   connection_profile_id,
                   name,
                   source_path,
                   destination_path,
                   polling_interval_seconds,
                   is_enabled,
                   last_polled_at
            FROM core.upsert_sftp_sync_job(
                @id,
                @connection_profile_id,
                @name,
                @source_path,
                @destination_path,
                @polling_interval_seconds,
                @is_enabled);
            """;

        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("id", job.Id ?? Guid.NewGuid());
        command.Parameters.AddWithValue("connection_profile_id", job.ConnectionProfileId);
        command.Parameters.AddWithValue("name", job.Name);
        command.Parameters.AddWithValue("source_path", job.SourcePath);
        command.Parameters.AddWithValue("destination_path", job.DestinationPath);
        command.Parameters.AddWithValue("polling_interval_seconds", job.PollingIntervalSeconds);
        command.Parameters.AddWithValue("is_enabled", job.IsEnabled);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            throw new InvalidOperationException("SFTP sync job upsert did not return a row.");
        }

        return ReadSftpSyncJob(reader);
    }

    private static SftpSyncJob ReadSftpSyncJob(NpgsqlDataReader reader)
    {
        return new SftpSyncJob
        {
            Id = reader.GetGuid(0),
            ConnectionProfileId = reader.GetGuid(1),
            Name = reader.GetString(2),
            SourcePath = reader.GetString(3),
            DestinationPath = reader.GetString(4),
            PollingIntervalSeconds = reader.GetInt32(5),
            IsEnabled = reader.GetBoolean(6),
            LastPolledAt = reader.IsDBNull(7) ? null : reader.GetFieldValue<DateTimeOffset>(7)
        };
    }
}
