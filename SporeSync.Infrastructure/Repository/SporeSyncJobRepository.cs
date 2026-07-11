using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using SporeSync.Domain.Interface;
using SporeSync.Domain.Model;
using SporeSync.Infrastructure.Logging;

namespace SporeSync.Infrastructure.Repository;

public sealed class SporeSyncJobRepository : ISporeSyncJobRepository
{
    private const string OpGetAllJobs = "GetAllJobs";
    private const string OpGetJobById = "GetJobById";
    private const string OpUpsertJob = "UpsertJob";
    private const string OpGetDueJobs = "GetDueJobs";
    private const string OpMarkJobPolled = "MarkJobPolled";
    private const string OpDeleteJob = "DeleteJob";
    private const string OpCountJobsForProfile = "CountJobsForProfile";

    private readonly NpgsqlDataSource _dataSource;
    private readonly ILogger<SporeSyncJobRepository> _logger;

    public SporeSyncJobRepository(
        NpgsqlDataSource dataSource,
        ILogger<SporeSyncJobRepository>? logger = null)
    {
        _dataSource = dataSource;
        _logger = logger ?? NullLogger<SporeSyncJobRepository>.Instance;
    }

    public async Task<IReadOnlyCollection<SporeSyncJob>> GetAllAsync(
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

        var jobs = new List<SporeSyncJob>();

        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        await using var command = new NpgsqlCommand(sql, connection);

        return await DbCommandLogger.ExecuteReaderAsync(_logger, command, OpGetAllJobs,
            async reader =>
            {
                while (await reader.ReadAsync(cancellationToken))
                {
                    jobs.Add(ReadSporeSyncJob(reader));
                }
                return jobs;
            }, cancellationToken);
    }

    public async Task<SporeSyncJob?> GetByIdAsync(
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
                return ReadSporeSyncJob(reader);
            }, cancellationToken);
    }

    public async Task<SporeSyncJob> UpsertAsync(
        UpsertSporeSyncJob job,
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
                return ReadSporeSyncJob(reader);
            }, cancellationToken);
    }

    public async Task<IReadOnlyCollection<SporeSyncJob>> GetDueJobsAsync(
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

        var jobs = new List<SporeSyncJob>();

        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        await using var command = new NpgsqlCommand(sql, connection);

        return await DbCommandLogger.ExecuteReaderAsync(_logger, command, OpGetDueJobs,
            async reader =>
            {
                while (await reader.ReadAsync(cancellationToken))
                {
                    jobs.Add(ReadSporeSyncJob(reader));
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

    public async Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        const string sql = "SELECT core.delete_sftp_sync_job(@id);";

        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("id", id);

        return await DbCommandLogger.ExecuteScalarAsync<bool>(_logger, command, OpDeleteJob, cancellationToken);
    }

    public async Task<SafeDeleteSporeSyncJobResult> SafeDeleteAsync(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        const string sql = "SELECT core.safe_delete_sftp_sync_job(@id);";

        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("id", id);

        var result = await DbCommandLogger.ExecuteScalarAsync<string>(_logger, command, OpDeleteJob, cancellationToken);
        return result switch
        {
            "deleted" => SafeDeleteSporeSyncJobResult.Deleted,
            "not_found" => SafeDeleteSporeSyncJobResult.NotFound,
            "active_run" => SafeDeleteSporeSyncJobResult.ActiveRunExists,
            _ => throw new InvalidOperationException($"Unexpected safe job deletion result '{result}'.")
        };
    }

    public async Task<int> CountByConnectionProfileAsync(
        Guid connectionProfileId,
        CancellationToken cancellationToken = default)
    {
        const string sql = "SELECT core.count_sftp_sync_jobs_for_connection_profile(@profile_id);";

        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("profile_id", connectionProfileId);

        return await DbCommandLogger.ExecuteScalarAsync<int>(_logger, command, OpCountJobsForProfile, cancellationToken);
    }

    private static SporeSyncJob ReadSporeSyncJob(NpgsqlDataReader reader)
    {
        return new SporeSyncJob
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
