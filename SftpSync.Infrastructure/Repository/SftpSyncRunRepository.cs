using Npgsql;
using NpgsqlTypes;
using SftpSync.Domain.Interface;
using SftpSync.Domain.Model;

namespace SftpSync.Infrastructure.Repository;

public sealed class SftpSyncRunRepository : ISftpSyncRunRepository
{
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

    public SftpSyncRunRepository(NpgsqlDataSource dataSource)
    {
        _dataSource = dataSource;
    }

    public async Task<PagedResult<SftpSyncRun>> GetRunsAsync(
        RunQuery query,
        CancellationToken cancellationToken = default)
    {
        var pageNumber = Math.Max(1, query.PageNumber);
        var pageSize = Math.Clamp(query.PageSize, 1, 200);
        var offset = (pageNumber - 1) * pageSize;
        var statuses = NormalizeStatuses(query.Statuses);
        var sortExpression = GetSortExpression(query.SortBy);
        var sortDirection = IsAscending(query.SortDirection) ? "ASC" : "DESC";

        var whereSql = """
            WHERE (@statuses::text[] IS NULL OR r.status = ANY(@statuses))
              AND (
                    @search IS NULL
                    OR j.name ILIKE @search
                    OR EXISTS (
                        SELECT 1
                        FROM core.download_queue_items qi
                        WHERE qi.sync_run_id = r.id
                          AND (qi.remote_path ILIKE @search OR qi.destination_path ILIKE @search)
                    )
                  )
            """;

        var countSql = $"""
            SELECT count(*)
            FROM core.sftp_sync_runs r
            INNER JOIN core.sftp_sync_jobs j ON j.id = r.job_id
            {whereSql};
            """;

        var itemsSql = $"""
            SELECT r.id,
                   r.job_id,
                   j.name AS job_name,
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
            FROM core.sftp_sync_runs r
            INNER JOIN core.sftp_sync_jobs j ON j.id = r.job_id
            {whereSql}
            ORDER BY {sortExpression} {sortDirection}, r.started_at DESC, r.id
            LIMIT @page_size OFFSET @offset;
            """;

        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);

        await using var countCommand = new NpgsqlCommand(countSql, connection);
        AddQueryParameters(countCommand, statuses, query.Search);
        var totalCount = (long)(await countCommand.ExecuteScalarAsync(cancellationToken)
            ?? throw new InvalidOperationException("Run count query did not return a value."));

        var runs = new List<SftpSyncRun>();
        await using var itemsCommand = new NpgsqlCommand(itemsSql, connection);
        AddQueryParameters(itemsCommand, statuses, query.Search);
        itemsCommand.Parameters.AddWithValue("page_size", pageSize);
        itemsCommand.Parameters.AddWithValue("offset", offset);

        await using var reader = await itemsCommand.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            runs.Add(ReadRun(reader));
        }

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
                   j.name AS job_name,
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
            FROM core.sftp_sync_runs r
            INNER JOIN core.sftp_sync_jobs j ON j.id = r.job_id
            WHERE r.id = @id;
            """;

        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("id", id);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        return ReadRun(reader);
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

    private static string GetSortExpression(string sortBy)
    {
        return sortBy switch
        {
            "status" => "r.status",
            "jobName" => "j.name",
            "size" => "r.total_bytes",
            "progress" => "CASE WHEN r.total_bytes = 0 THEN 0 ELSE r.downloaded_bytes::numeric / r.total_bytes END",
            "completedAt" => "r.completed_at",
            _ => "r.started_at"
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
