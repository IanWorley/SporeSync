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
        var totalCount = (long)(await countCommand.ExecuteScalarAsync(cancellationToken)
            ?? throw new InvalidOperationException("Run count query did not return a value."));

        var runs = new List<SftpSyncRun>();
        await using var itemsCommand = new NpgsqlCommand(sql, connection);
        AddQueryParameters(itemsCommand, statuses, query.Search);
        itemsCommand.Parameters.AddWithValue("sort_by", NormalizeSortBy(query.SortBy));
        itemsCommand.Parameters.AddWithValue("sort_direction", sortDirection);
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
