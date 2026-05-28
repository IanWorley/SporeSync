using Npgsql;
using NpgsqlTypes;
using SftpSync.Domain.Interface;
using SftpSync.Domain.Model;

namespace SftpSync.Infrastructure.Repository;

public sealed class DownloadQueueItemRepository : IDownloadQueueItemRepository
{
    private static readonly HashSet<string> AllowedStatuses = new(StringComparer.OrdinalIgnoreCase)
    {
        "queued",
        "comparing",
        "downloading",
        "completed",
        "failed",
        "skipped"
    };

    private readonly NpgsqlDataSource _dataSource;

    public DownloadQueueItemRepository(NpgsqlDataSource dataSource)
    {
        _dataSource = dataSource;
    }

    public async Task<PagedResult<DownloadQueueItem>> GetByRunIdAsync(
        Guid runId,
        QueueItemQuery query,
        CancellationToken cancellationToken = default)
    {
        var pageNumber = Math.Max(1, query.PageNumber);
        var pageSize = Math.Clamp(query.PageSize, 1, 200);
        var offset = (pageNumber - 1) * pageSize;
        var statuses = NormalizeStatuses(query.Statuses);
        var sortExpression = GetSortExpression(query.SortBy);
        var sortDirection = IsAscending(query.SortDirection) ? "ASC" : "DESC";

        const string whereSql = """
            WHERE sync_run_id = @run_id
              AND (@statuses::text[] IS NULL OR status = ANY(@statuses))
              AND (@search IS NULL OR remote_path ILIKE @search OR destination_path ILIKE @search)
            """;

        var countSql = $"""
            SELECT count(*)
            FROM core.download_queue_items
            {whereSql};
            """;

        var itemsSql = $"""
            SELECT id,
                   job_id,
                   sync_run_id,
                   remote_path,
                   destination_path,
                   file_size_bytes,
                   remote_modified_at,
                   status,
                   bytes_downloaded,
                   current_bytes_per_second,
                   retry_count,
                   handled_reason,
                   error_message,
                   queued_at,
                   started_at,
                   completed_at,
                   updated_at
            FROM core.download_queue_items
            {whereSql}
            ORDER BY {sortExpression} {sortDirection}, queued_at DESC, id
            LIMIT @page_size OFFSET @offset;
            """;

        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);

        await using var countCommand = new NpgsqlCommand(countSql, connection);
        AddQueryParameters(countCommand, runId, statuses, query.Search);
        var totalCount = (long)(await countCommand.ExecuteScalarAsync(cancellationToken)
            ?? throw new InvalidOperationException("Queue item count query did not return a value."));

        var items = new List<DownloadQueueItem>();
        await using var itemsCommand = new NpgsqlCommand(itemsSql, connection);
        AddQueryParameters(itemsCommand, runId, statuses, query.Search);
        itemsCommand.Parameters.AddWithValue("page_size", pageSize);
        itemsCommand.Parameters.AddWithValue("offset", offset);

        await using var reader = await itemsCommand.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            items.Add(ReadItem(reader));
        }

        return new PagedResult<DownloadQueueItem>
        {
            Items = items,
            PageNumber = pageNumber,
            PageSize = pageSize,
            TotalCount = totalCount
        };
    }

    private static void AddQueryParameters(
        NpgsqlCommand command,
        Guid runId,
        string[]? statuses,
        string? search)
    {
        command.Parameters.AddWithValue("run_id", runId);
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
            "status" => "status",
            "basename" => "regexp_replace(remote_path, '^.*/', '')",
            "path" => "remote_path",
            "size" => "file_size_bytes",
            "progress" => "CASE WHEN file_size_bytes = 0 THEN 0 ELSE bytes_downloaded::numeric / file_size_bytes END",
            "completedAt" => "completed_at",
            _ => "queued_at"
        };
    }

    private static bool IsAscending(string direction)
    {
        return string.Equals(direction, "asc", StringComparison.OrdinalIgnoreCase);
    }

    private static DownloadQueueItem ReadItem(NpgsqlDataReader reader)
    {
        return new DownloadQueueItem
        {
            Id = reader.GetGuid(0),
            JobId = reader.GetGuid(1),
            SyncRunId = reader.IsDBNull(2) ? null : reader.GetGuid(2),
            RemotePath = reader.GetString(3),
            DestinationPath = reader.GetString(4),
            FileSizeBytes = reader.GetInt64(5),
            RemoteModifiedAt = reader.IsDBNull(6) ? null : reader.GetFieldValue<DateTimeOffset>(6),
            Status = reader.GetString(7),
            BytesDownloaded = reader.GetInt64(8),
            CurrentBytesPerSecond = reader.IsDBNull(9) ? null : reader.GetDecimal(9),
            RetryCount = reader.GetInt32(10),
            HandledReason = reader.IsDBNull(11) ? null : reader.GetString(11),
            ErrorMessage = reader.IsDBNull(12) ? null : reader.GetString(12),
            QueuedAt = reader.GetFieldValue<DateTimeOffset>(13),
            StartedAt = reader.IsDBNull(14) ? null : reader.GetFieldValue<DateTimeOffset>(14),
            CompletedAt = reader.IsDBNull(15) ? null : reader.GetFieldValue<DateTimeOffset>(15),
            UpdatedAt = reader.GetFieldValue<DateTimeOffset>(16)
        };
    }
}
