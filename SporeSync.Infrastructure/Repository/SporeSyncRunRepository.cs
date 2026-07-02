using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using NpgsqlTypes;
using SporeSync.Domain.Interface;
using SporeSync.Domain.Model;
using SporeSync.Infrastructure.Logging;

namespace SporeSync.Infrastructure.Repository;

public sealed class SporeSyncRunRepository : ISporeSyncRunRepository
{
    private const string OpCountRuns = "CountRuns";
    private const string OpGetRuns = "GetRuns";
    private const string OpGetRunById = "GetRunById";
    private const string OpCreateRun = "CreateRun";
    private const string OpUpdateRunStatus = "UpdateRunStatus";
    private const string OpJobHasActiveRun = "JobHasActiveRun";
    private const string OpRecalculateAggregates = "RecalculateAggregates";
    private const string OpRunHasPendingDownloads = "RunHasPendingDownloads";
    private const string OpRenewRunLease = "RenewRunLease";
    private const string OpPruneHistory = "PruneHistory";
    private const string OpReapOrphanedRuns = "ReapOrphanedRuns";
    private const string OpRetryFailedItems = "RetryFailedItems";
    private const string OpCancelRun = "CancelRun";

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
    private readonly ILogger<SporeSyncRunRepository> _logger;

    public SporeSyncRunRepository(
        NpgsqlDataSource dataSource,
        ILogger<SporeSyncRunRepository>? logger = null)
    {
        _dataSource = dataSource;
        _logger = logger ?? NullLogger<SporeSyncRunRepository>.Instance;
    }

    public async Task<PagedResult<SporeSyncRun>> GetRunsAsync(
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
                var items = new List<SporeSyncRun>();
                while (await reader.ReadAsync(cancellationToken))
                {
                    items.Add(ReadRun(reader));
                }
                return items;
            }, cancellationToken);

        return new PagedResult<SporeSyncRun>
        {
            Items = runs,
            PageNumber = pageNumber,
            PageSize = pageSize,
            TotalCount = totalCount
        };
    }

    public async Task<SporeSyncRun?> GetByIdAsync(
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

    public async Task<SporeSyncRun?> CreateAsync(
        Guid jobId,
        int leaseSeconds = 1800,
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
            FROM core.create_sftp_sync_run(@job_id, @lease_seconds) r;
            """;

        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("job_id", jobId);
        command.Parameters.AddWithValue("lease_seconds", leaseSeconds);

        return await DbCommandLogger.ExecuteReaderAsync(_logger, command, OpCreateRun,
            async reader =>
            {
                if (!await reader.ReadAsync(cancellationToken))
                {
                    return (SporeSyncRun?)null;
                }
                return ReadRun(reader);
            }, cancellationToken);
    }

    public async Task<SporeSyncRun> UpdateStatusAsync(
        UpdateSporeSyncRunStatus update,
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
                @error_message,
                @lease_seconds) r;
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
        command.Parameters.AddWithValue("lease_seconds", (object?)update.LeaseSeconds ?? DBNull.Value);

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

    public async Task<SporeSyncRun> RecalculateAggregatesAsync(
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

    public async Task<bool> RenewLeaseAsync(
        Guid runId,
        int leaseSeconds,
        CancellationToken cancellationToken = default)
    {
        const string sql = "SELECT core.renew_sftp_sync_run_lease(@id, @lease_seconds);";

        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("id", runId);
        command.Parameters.AddWithValue("lease_seconds", leaseSeconds);

        return await DbCommandLogger.ExecuteScalarAsync<bool>(_logger, command, OpRenewRunLease, cancellationToken);
    }

    public async Task<SyncHistoryPruneResult> PruneHistoryAsync(
        DateTimeOffset cutoff,
        CancellationToken cancellationToken = default)
    {
        const string sql = """
            SELECT p.pruned_run_count,
                   p.pruned_queue_item_count
            FROM core.prune_sftp_sync_history(@cutoff) p;
            """;

        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("cutoff", cutoff);

        return await DbCommandLogger.ExecuteReaderAsync(_logger, command, OpPruneHistory,
            async reader =>
            {
                if (!await reader.ReadAsync(cancellationToken))
                {
                    throw new InvalidOperationException("Sync history prune did not return a row.");
                }
                return new SyncHistoryPruneResult(reader.GetInt32(0), reader.GetInt32(1));
            }, cancellationToken);
    }

    public async Task<IReadOnlyList<SporeSyncRun>> ReapOrphanedAsync(
        bool ignoreLeases,
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
            FROM core.reap_orphaned_sftp_sync_runs(@ignore_lease) r;
            """;

        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("ignore_lease", ignoreLeases);

        return await DbCommandLogger.ExecuteReaderAsync(_logger, command, OpReapOrphanedRuns,
            async reader =>
            {
                var runs = new List<SporeSyncRun>();
                while (await reader.ReadAsync(cancellationToken))
                {
                    runs.Add(ReadRun(reader));
                }
                return runs;
            }, cancellationToken);
    }

    public async Task<SporeSyncRun?> CancelAsync(
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
            FROM core.cancel_sftp_sync_run(@run_id) r;
            """;

        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("run_id", runId);

        return await DbCommandLogger.ExecuteReaderAsync(_logger, command, OpCancelRun,
            async reader =>
            {
                if (!await reader.ReadAsync(cancellationToken))
                {
                    return null;
                }
                return ReadRun(reader);
            }, cancellationToken);
    }

    public async Task<int> RetryFailedItemsAsync(
        Guid runId,
        CancellationToken cancellationToken = default)
    {
        const string sql = "SELECT core.retry_failed_download_queue_items(@run_id);";

        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("run_id", runId);

        return await DbCommandLogger.ExecuteScalarAsync<int>(_logger, command, OpRetryFailedItems, cancellationToken);
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

    private static SporeSyncRun ReadRun(NpgsqlDataReader reader)
    {
        return new SporeSyncRun
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
