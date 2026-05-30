using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using NpgsqlTypes;
using SftpSync.Domain.Interface;
using SftpSync.Domain.Model;
using SftpSync.Infrastructure.Logging;

namespace SftpSync.Infrastructure.Repository;

public sealed class SftpSyncRunRepository : ISftpSyncRunRepository
{
    private const string OpCountRuns = "CountRuns";
    private const string OpGetRuns = "GetRuns";
    private const string OpGetRunById = "GetRunById";
    private const string OpCreateRun = "CreateRun";
    private const string OpUpdateRunStatus = "UpdateRunStatus";
    private const string OpJobHasActiveRun = "JobHasActiveRun";
    private const string OpRecalculateAggregates = "RecalculateAggregates";
    private const string OpRunHasPendingDownloads = "RunHasPendingDownloads";

    private static readonly HashSet<string> AllowedStatuses = new(StringComparer.OrdinalIgnoreCase)
    {
        "queued",
        "scanning",
        "downloading",
        "completed",
        "failed",
        "cancelled"
    };

    private readonly NpgsqlDataSource _dataSource;
    private readonly ILogger<SftpSyncRunRepository> _logger;

    public SftpSyncRunRepository(
        NpgsqlDataSource dataSource,
        ILogger<SftpSyncRunRepository>? logger = null)
    {
        _dataSource = dataSource;
        _logger = logger ?? NullLogger<SftpSyncRunRepository>.Instance;
    }

    public async Task<PagedResult<SftpSyncRun>> GetRunsAsync(
        RunQuery query,
        CancellationToken cancellationToken = default)
    {
        var pageNumber = Math.Max(1, query.PageNumber);
        var pageSize = Math.Clamp(query.PageSize, 1, 200);
        var offset = (pageNumber - 1) * pageSize;
        var statuses = NormalizeStatuses(query.Statuses);
        var sortDirection = IsAscending(query.SortDirection) ? "ASC" : "DESC";

        const string sql = """
            SELECT r.id,
                   r.job_id,
                   r.job_name,
                   r.status,
                   r.started_at,
                   r.completed_at,
                   r.total_file_count,
                   r.completed_file_count,
                   r.skipped_file_count,
                   r.failed_file_count,
                   r.total_bytes,
                   r.downloaded_bytes,
                   r.current_bytes_per_second,
                   r.error_message
            FROM core.get_sftp_sync_runs(
                @statuses,
                @search,
                @sort_by,
                @sort_direction,
                @page_size,
                @offset) r;
            """;

        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);

        const string countSql = "SELECT core.count_sftp_sync_runs(@statuses, @search);";
        await using var countCommand = new NpgsqlCommand(countSql, connection);
        AddQueryParameters(countCommand, statuses, query.Search);
        var totalCount = (long)(await DbCommandLogger.ExecuteScalarAsync(_logger, countCommand, OpCountRuns, cancellationToken)
            ?? throw new InvalidOperationException("Run count query did not return a value."));

        await using var itemsCommand = new NpgsqlCommand(sql, connection);
        AddQueryParameters(itemsCommand, statuses, query.Search);
        itemsCommand.Parameters.AddWithValue("sort_by", NormalizeSortBy(query.SortBy));
        itemsCommand.Parameters.AddWithValue("sort_direction", sortDirection);
        itemsCommand.Parameters.AddWithValue("page_size", pageSize);
        itemsCommand.Parameters.AddWithValue("offset", offset);

        var runs = await DbCommandLogger.ExecuteReaderAsync(_logger, itemsCommand, OpGetRuns,
            async reader =>
            {
                var items = new List<SftpSyncRun>();
                while (await reader.ReadAsync(cancellationToken))
                {
                    items.Add(ReadRun(reader));
                }
                return items;
            }, cancellationToken);

        return new PagedResult<SftpSyncRun>
        {
            Items = runs,
            PageNumber = pageNumber,
            PageSize = pageSize,
            TotalCount = totalCount
        };
    }

    public async Task<SftpSyncRun?> GetByIdAsync(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        const string sql = """
            SELECT r.id,
                   r.job_id,
                   r.job_name,
                   r.status,
                   r.started_at,
                   r.completed_at,
                   r.total_file_count,
                   r.completed_file_count,
                   r.skipped_file_count,
                   r.failed_file_count,
                   r.total_bytes,
                   r.downloaded_bytes,
                   r.current_bytes_per_second,
                   r.error_message
            FROM core.get_sftp_sync_run(@id) r;
            """;

        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("id", id);

        return await DbCommandLogger.ExecuteReaderAsync(_logger, command, OpGetRunById,
            async reader =>
            {
                if (!await reader.ReadAsync(cancellationToken))
                {
                    return null;
                }
                return ReadRun(reader);
            }, cancellationToken);
    }

    public async Task<SftpSyncRun> CreateAsync(
        Guid jobId,
        CancellationToken cancellationToken = default)
    {
        const string sql = """
            SELECT r.id,
                   r.job_id,
                   r.job_name,
                   r.status,
                   r.started_at,
                   r.completed_at,
                   r.total_file_count,
                   r.completed_file_count,
                   r.skipped_file_count,
                   r.failed_file_count,
                   r.total_bytes,
                   r.downloaded_bytes,
                   r.current_bytes_per_second,
                   r.error_message
            FROM core.create_sftp_sync_run(@job_id) r;
            """;

        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("job_id", jobId);

        return await DbCommandLogger.ExecuteReaderAsync(_logger, command, OpCreateRun,
            async reader =>
            {
                if (!await reader.ReadAsync(cancellationToken))
                {
                    throw new InvalidOperationException("SFTP sync run create did not return a row.");
                }
                return ReadRun(reader);
            }, cancellationToken);
    }

    public async Task<SftpSyncRun> UpdateStatusAsync(
        UpdateSftpSyncRunStatus update,
        CancellationToken cancellationToken = default)
    {
        const string sql = """
            SELECT r.id,
                   r.job_id,
                   r.job_name,
                   r.status,
                   r.started_at,
                   r.completed_at,
                   r.total_file_count,
                   r.completed_file_count,
                   r.skipped_file_count,
                   r.failed_file_count,
                   r.total_bytes,
                   r.downloaded_bytes,
                   r.current_bytes_per_second,
                   r.error_message
            FROM core.update_sftp_sync_run_status(
                @id,
                @status,
                @total_file_count,
                @total_bytes,
                @completed_file_count,
                @skipped_file_count,
                @failed_file_count,
                @downloaded_bytes,
                @current_bytes_per_second,
                @error_message) r;
            """;

        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("id", update.Id);
        command.Parameters.AddWithValue("status", update.Status);
        command.Parameters.AddWithValue("total_file_count", (object?)update.TotalFileCount ?? DBNull.Value);
        command.Parameters.AddWithValue("total_bytes", (object?)update.TotalBytes ?? DBNull.Value);
        command.Parameters.AddWithValue("completed_file_count", (object?)update.CompletedFileCount ?? DBNull.Value);
        command.Parameters.AddWithValue("skipped_file_count", (object?)update.SkippedFileCount ?? DBNull.Value);
        command.Parameters.AddWithValue("failed_file_count", (object?)update.FailedFileCount ?? DBNull.Value);
        command.Parameters.AddWithValue("downloaded_bytes", (object?)update.DownloadedBytes ?? DBNull.Value);
        command.Parameters.AddWithValue("current_bytes_per_second", (object?)update.CurrentBytesPerSecond ?? DBNull.Value);
        command.Parameters.AddWithValue("error_message", (object?)update.ErrorMessage ?? DBNull.Value);

        return await DbCommandLogger.ExecuteReaderAsync(_logger, command, OpUpdateRunStatus,
            async reader =>
            {
                if (!await reader.ReadAsync(cancellationToken))
                {
                    throw new InvalidOperationException($"SFTP sync run '{update.Id}' was not found when updating status.");
                }
                return ReadRun(reader);
            }, cancellationToken);
    }

    public async Task<bool> HasActiveRunAsync(
        Guid jobId,
        CancellationToken cancellationToken = default)
    {
        const string sql = "SELECT core.job_has_active_run(@job_id);";

        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("job_id", jobId);

        return await DbCommandLogger.ExecuteScalarAsync<bool>(_logger, command, OpJobHasActiveRun, cancellationToken);
    }

    public async Task<SftpSyncRun> RecalculateAggregatesAsync(
        Guid runId,
        CancellationToken cancellationToken = default)
    {
        const string sql = """
            SELECT r.id,
                   r.job_id,
                   r.job_name,
                   r.status,
                   r.started_at,
                   r.completed_at,
                   r.total_file_count,
                   r.completed_file_count,
                   r.skipped_file_count,
                   r.failed_file_count,
                   r.total_bytes,
                   r.downloaded_bytes,
                   r.current_bytes_per_second,
                   r.error_message
            FROM core.recalculate_sftp_sync_run_aggregates(@run_id) r;
            """;

        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("run_id", runId);

        return await DbCommandLogger.ExecuteReaderAsync(_logger, command, OpRecalculateAggregates,
            async reader =>
            {
                if (!await reader.ReadAsync(cancellationToken))
                {
                    throw new InvalidOperationException($"SFTP sync run '{runId}' was not found when recalculating aggregates.");
                }
                return ReadRun(reader);
            }, cancellationToken);
    }

    public async Task<bool> HasPendingDownloadsAsync(
        Guid runId,
        CancellationToken cancellationToken = default)
    {
        const string sql = "SELECT core.run_has_pending_downloads(@run_id);";

        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("run_id", runId);

        return await DbCommandLogger.ExecuteScalarAsync<bool>(_logger, command, OpRunHasPendingDownloads, cancellationToken);
    }

    private static void AddQueryParameters(
        NpgsqlCommand command,
        string[]? statuses,
        string? search)
    {
        command.Parameters.Add(new NpgsqlParameter<string[]?>("statuses", NpgsqlDbType.Array | NpgsqlDbType.Text)
        {
            TypedValue = statuses
        });
        command.Parameters.Add(new NpgsqlParameter<string?>("search", NpgsqlDbType.Text)
        {
            TypedValue = string.IsNullOrWhiteSpace(search) ? null : $"%{search.Trim()}%"
        });
    }

    private static string[]? NormalizeStatuses(IReadOnlyCollection<string> statuses)
    {
        var normalized = statuses
            .Where(status => AllowedStatuses.Contains(status))
            .Select(status => status.ToLowerInvariant())
            .Distinct()
            .ToArray();

        return normalized.Length == 0 ? null : normalized;
    }

    private static string NormalizeSortBy(string sortBy)
    {
        return sortBy switch
        {
            "status" or "jobName" or "size" or "progress" or "completedAt" => sortBy,
            _ => "startedAt"
        };
    }

    private static bool IsAscending(string direction)
    {
        return string.Equals(direction, "asc", StringComparison.OrdinalIgnoreCase);
    }

    private static SftpSyncRun ReadRun(NpgsqlDataReader reader)
    {
        return new SftpSyncRun
        {
            Id = reader.GetGuid(0),
            JobId = reader.GetGuid(1),
            JobName = reader.GetString(2),
            Status = reader.GetString(3),
            StartedAt = reader.GetFieldValue<DateTimeOffset>(4),
            CompletedAt = reader.IsDBNull(5) ? null : reader.GetFieldValue<DateTimeOffset>(5),
            TotalFileCount = reader.GetInt32(6),
            CompletedFileCount = reader.GetInt32(7),
            SkippedFileCount = reader.GetInt32(8),
            FailedFileCount = reader.GetInt32(9),
            TotalBytes = reader.GetInt64(10),
            DownloadedBytes = reader.GetInt64(11),
            CurrentBytesPerSecond = reader.IsDBNull(12) ? null : reader.GetDecimal(12),
            ErrorMessage = reader.IsDBNull(13) ? null : reader.GetString(13)
        };
    }
}
