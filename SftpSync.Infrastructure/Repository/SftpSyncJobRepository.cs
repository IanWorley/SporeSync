using Microsoft.Extensions.Logging;
using Npgsql;
using SftpSync.Domain.Interface;
using SftpSync.Domain.Model;
using SftpSync.Infrastructure.Logging;

namespace SftpSync.Infrastructure.Repository;

public sealed class SftpSyncJobRepository : ISftpSyncJobRepository
{
    private const string OpGetAllJobs = "GetAllJobs";
    private const string OpGetJobById = "GetJobById";
    private const string OpUpsertJob = "UpsertJob";
    private const string OpGetDueJobs = "GetDueJobs";
    private const string OpMarkJobPolled = "MarkJobPolled";

    private readonly NpgsqlDataSource _dataSource;
    private readonly ILogger<SftpSyncJobRepository> _logger;

    public SftpSyncJobRepository(NpgsqlDataSource dataSource, ILogger<SftpSyncJobRepository> logger)
    {
        _dataSource = dataSource;
        _logger = logger;
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

        return await DbCommandLogger.ExecuteReaderAsync(_logger, command, OpGetAllJobs,
            async reader =>
            {
                while (await reader.ReadAsync(cancellationToken))
                {
                    jobs.Add(ReadSftpSyncJob(reader));
                }
                return jobs;
            }, cancellationToken);
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

        return await DbCommandLogger.ExecuteReaderAsync(_logger, command, OpGetJobById,
            async reader =>
            {
                if (!await reader.ReadAsync(cancellationToken))
                {
                    return null;
                }
                return ReadSftpSyncJob(reader);
            }, cancellationToken);
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

        return await DbCommandLogger.ExecuteReaderAsync(_logger, command, OpUpsertJob,
            async reader =>
            {
                if (!await reader.ReadAsync(cancellationToken))
                {
                    throw new InvalidOperationException("SFTP sync job upsert did not return a row.");
                }
                return ReadSftpSyncJob(reader);
            }, cancellationToken);
    }

    public async Task<IReadOnlyCollection<SftpSyncJob>> GetDueJobsAsync(
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
            FROM core.get_due_sftp_sync_jobs();
            """;

        var jobs = new List<SftpSyncJob>();

        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        await using var command = new NpgsqlCommand(sql, connection);

        return await DbCommandLogger.ExecuteReaderAsync(_logger, command, OpGetDueJobs,
            async reader =>
            {
                while (await reader.ReadAsync(cancellationToken))
                {
                    jobs.Add(ReadSftpSyncJob(reader));
                }
                return jobs;
            }, cancellationToken);
    }

    public async Task MarkPolledAsync(Guid id, CancellationToken cancellationToken = default)
    {
        const string sql = """
            SELECT id
            FROM core.mark_sftp_sync_job_polled(@id);
            """;

        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("id", id);

        await DbCommandLogger.ExecuteReaderAsync(_logger, command, OpMarkJobPolled,
            async reader =>
            {
                if (!await reader.ReadAsync(cancellationToken))
                {
                    throw new InvalidOperationException($"SFTP sync job '{id}' was not found when marking polled.");
                }
                return true;
            }, cancellationToken);
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
