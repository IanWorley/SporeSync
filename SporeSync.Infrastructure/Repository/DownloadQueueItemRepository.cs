using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using NpgsqlTypes;
using SporeSync.Domain.Interface;
using SporeSync.Domain.Model;
using SporeSync.Infrastructure.Logging;

namespace SporeSync.Infrastructure.Repository;

public sealed class DownloadQueueItemRepository : IDownloadQueueItemRepository
{
    private const string OpCountQueueItems = "CountQueueItems";
    private const string OpGetQueueItems = "GetQueueItems";
    private const string OpGetGroupLeaves = "GetGroupLeaves";
    private const string OpUpsertQueueItem = "UpsertQueueItem";
    private const string OpGetSyncedRemoteState = "GetSyncedRemoteState";
    private const string OpClaimNextQueueItem = "ClaimNextQueueItem";
    private const string OpUpdateQueueItemProgress = "UpdateQueueItemProgress";
    private const string OpMarkRemoteDeletedItems = "MarkRemoteDeletedItems";
    private const string OpRequeueFailedItems = "RequeueFailedItems";

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
    private readonly ILogger<DownloadQueueItemRepository> _logger;

    public DownloadQueueItemRepository(
        NpgsqlDataSource dataSource,
        ILogger<DownloadQueueItemRepository>? logger = null)
    {
        _dataSource = dataSource;
        _logger = logger ?? NullLogger<DownloadQueueItemRepository>.Instance;
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
        var sortDirection = IsAscending(query.SortDirection) ? "ASC" : "DESC";

        const string sql = """
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
                   updated_at,
                   is_group,
                   group_remote_path,
                   child_count
            FROM core.get_download_queue_items(
                @run_id,
                @statuses,
                @search,
                @sort_by,
                @sort_direction,
                @page_size,
                @offset);
            """;

        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);

        const string countSql = "SELECT core.count_download_queue_items(@run_id, @statuses, @search);";
        await using var countCommand = new NpgsqlCommand(countSql, connection);
        AddQueryParameters(countCommand, runId, statuses, query.Search);
        var totalCount = (long)(await DbCommandLogger.ExecuteScalarAsync(_logger, countCommand, OpCountQueueItems, cancellationToken)
            ?? throw new InvalidOperationException("Queue item count query did not return a value."));

        await using var itemsCommand = new NpgsqlCommand(sql, connection);
        AddQueryParameters(itemsCommand, runId, statuses, query.Search);
        itemsCommand.Parameters.AddWithValue("sort_by", NormalizeSortBy(query.SortBy));
        itemsCommand.Parameters.AddWithValue("sort_direction", sortDirection);
        itemsCommand.Parameters.AddWithValue("page_size", pageSize);
        itemsCommand.Parameters.AddWithValue("offset", offset);

        var items = await DbCommandLogger.ExecuteReaderAsync(_logger, itemsCommand, OpGetQueueItems,
            async reader =>
            {
                var results = new List<DownloadQueueItem>();
                while (await reader.ReadAsync(cancellationToken))
                {
                    results.Add(ReadItem(reader));
                }
                return results;
            }, cancellationToken);

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

    private static string NormalizeSortBy(string sortBy)
    {
        return sortBy switch
        {
            "status" or "basename" or "path" or "size" or "progress" or "completedAt" => sortBy,
            _ => "queuedAt"
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
            UpdatedAt = reader.GetFieldValue<DateTimeOffset>(16),
            IsGroup = reader.GetBoolean(17),
            GroupRemotePath = reader.IsDBNull(18) ? null : reader.GetString(18),
            ChildCount = reader.GetInt32(19)
        };
    }

    public async Task<IReadOnlyList<DownloadQueueItem>> GetLeavesForGroupAsync(
        Guid runId,
        string groupRemotePath,
        CancellationToken cancellationToken = default)
    {
        const string sql = """
            SELECT id, job_id, sync_run_id, remote_path, destination_path, file_size_bytes,
                   remote_modified_at, status, bytes_downloaded, current_bytes_per_second,
                   retry_count, handled_reason, error_message, queued_at, started_at,
                   completed_at, updated_at, is_group, group_remote_path, child_count
            FROM core.get_download_queue_group_leaves(@run_id, @group_remote_path);
            """;

        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("run_id", runId);
        command.Parameters.AddWithValue("group_remote_path", groupRemotePath);

        return await DbCommandLogger.ExecuteReaderAsync(_logger, command, OpGetGroupLeaves,
            async reader =>
            {
                var items = new List<DownloadQueueItem>();
                while (await reader.ReadAsync(cancellationToken))
                {
                    items.Add(ReadItem(reader));
                }
                return items;
            }, cancellationToken);
    }

    public async Task<DownloadQueueItem?> GetByIdAsync(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        const string sql = """
            SELECT id, job_id, sync_run_id, remote_path, destination_path, file_size_bytes,
                   remote_modified_at, status, bytes_downloaded, current_bytes_per_second,
                   retry_count, handled_reason, error_message, queued_at, started_at,
                   completed_at, updated_at, is_group, group_remote_path, child_count
            FROM core.download_queue_items
            WHERE id = @id;
            """;

        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("id", id);

        return await DbCommandLogger.ExecuteReaderAsync(_logger, command, OpGetQueueItems,
            async reader =>
            {
                if (!await reader.ReadAsync(cancellationToken))
                {
                    return null;
                }

                return ReadItem(reader);
            }, cancellationToken);
    }

    public async Task<DownloadQueueItem> UpsertAsync(
        UpsertDownloadQueueItem item,
        CancellationToken cancellationToken = default)
    {
        const string sql = """
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
                   updated_at,
                   is_group,
                   group_remote_path,
                   child_count
            FROM core.upsert_download_queue_item(
                @job_id,
                @sync_run_id,
                @remote_path,
                @destination_path,
                @file_size_bytes,
                @remote_modified_at,
                @is_group,
                @group_remote_path,
                @child_count);
            """;

        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("job_id", item.JobId);
        command.Parameters.AddWithValue("sync_run_id", item.SyncRunId);
        command.Parameters.AddWithValue("remote_path", item.RemotePath);
        command.Parameters.AddWithValue("destination_path", item.DestinationPath);
        command.Parameters.AddWithValue("file_size_bytes", item.FileSizeBytes);
        command.Parameters.AddWithValue("remote_modified_at", (object?)item.RemoteModifiedAt ?? DBNull.Value);
        command.Parameters.AddWithValue("is_group", item.IsGroup);
        command.Parameters.AddWithValue("group_remote_path", (object?)item.GroupRemotePath ?? DBNull.Value);
        command.Parameters.AddWithValue("child_count", item.ChildCount);

        return await DbCommandLogger.ExecuteReaderAsync(_logger, command, OpUpsertQueueItem,
            async reader =>
            {
                if (!await reader.ReadAsync(cancellationToken))
                {
                    throw new InvalidOperationException("Download queue item upsert did not return a row.");
                }
                return ReadItem(reader);
            }, cancellationToken);
    }

    public async Task<IReadOnlyDictionary<string, SyncedRemoteState>> GetSyncedStateAsync(
        Guid jobId,
        CancellationToken cancellationToken = default)
    {
        const string sql = """
            SELECT remote_path,
                   remote_modified_at,
                   file_size_bytes,
                   status
            FROM core.get_synced_remote_state(@job_id);
            """;

        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("job_id", jobId);

        return await DbCommandLogger.ExecuteReaderAsync(_logger, command, OpGetSyncedRemoteState,
            async reader =>
            {
                var states = new Dictionary<string, SyncedRemoteState>(StringComparer.Ordinal);
                while (await reader.ReadAsync(cancellationToken))
                {
                    var state = new SyncedRemoteState
                    {
                        RemotePath = reader.GetString(0),
                        RemoteModifiedAt = reader.IsDBNull(1) ? null : reader.GetFieldValue<DateTimeOffset>(1),
                        FileSizeBytes = reader.GetInt64(2),
                        Status = reader.GetString(3)
                    };
                    states[state.RemotePath] = state;
                }
                return states;
            }, cancellationToken);
    }

    public async Task<DownloadQueueItem?> ClaimNextAsync(CancellationToken cancellationToken = default)
    {
        const string sql = """
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
                   updated_at,
                   is_group,
                   group_remote_path,
                   child_count
            FROM core.claim_next_download_queue_item();
            """;

        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        await using var command = new NpgsqlCommand(sql, connection);

        return await DbCommandLogger.ExecuteReaderAsync(_logger, command, OpClaimNextQueueItem,
            async reader =>
            {
                if (!await reader.ReadAsync(cancellationToken))
                {
                    return null;
                }
                return ReadItem(reader);
            }, cancellationToken);
    }

    public async Task<DownloadQueueItem> UpdateProgressAsync(
        UpdateDownloadQueueItemProgress update,
        CancellationToken cancellationToken = default)
    {
        const string sql = """
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
                   updated_at,
                   is_group,
                   group_remote_path,
                   child_count
            FROM core.update_download_queue_item_progress(
                @id,
                @status,
                @bytes_downloaded,
                @current_bytes_per_second,
                @error_message,
                @handled_reason);
            """;

        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("id", update.Id);
        command.Parameters.AddWithValue("status", update.Status);
        command.Parameters.AddWithValue("bytes_downloaded", update.BytesDownloaded);
        command.Parameters.AddWithValue("current_bytes_per_second", (object?)update.CurrentBytesPerSecond ?? DBNull.Value);
        command.Parameters.AddWithValue("error_message", (object?)update.ErrorMessage ?? DBNull.Value);
        command.Parameters.AddWithValue("handled_reason", (object?)update.HandledReason ?? DBNull.Value);

        return await DbCommandLogger.ExecuteReaderAsync(_logger, command, OpUpdateQueueItemProgress,
            async reader =>
            {
                if (!await reader.ReadAsync(cancellationToken))
                {
                    throw new InvalidOperationException($"Download queue item '{update.Id}' was not found when updating progress.");
                }
                return ReadItem(reader);
            }, cancellationToken);
    }

    public async Task<IReadOnlyList<DownloadQueueItem>> MarkRemoteDeletedAsync(
        Guid jobId,
        Guid syncRunId,
        IReadOnlyCollection<string> remotePaths,
        CancellationToken cancellationToken = default)
    {
        if (remotePaths.Count == 0)
        {
            return Array.Empty<DownloadQueueItem>();
        }

        const string sql = """
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
                   updated_at,
                   is_group,
                   group_remote_path,
                   child_count
            FROM core.mark_remote_deleted_download_queue_items(
                @job_id,
                @sync_run_id,
                @remote_paths);
            """;

        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("job_id", jobId);
        command.Parameters.AddWithValue("sync_run_id", syncRunId);
        command.Parameters.Add(new NpgsqlParameter<string[]>("remote_paths", NpgsqlDbType.Array | NpgsqlDbType.Text)
        {
            TypedValue = remotePaths.Distinct(StringComparer.Ordinal).ToArray()
        });

        return await DbCommandLogger.ExecuteReaderAsync(_logger, command, OpMarkRemoteDeletedItems,
            async reader =>
            {
                var items = new List<DownloadQueueItem>();
                while (await reader.ReadAsync(cancellationToken))
                {
                    items.Add(ReadItem(reader));
                }
                return items;
            }, cancellationToken);
    }

    public async Task<int> RequeueFailedAsync(
        Guid jobId,
        Guid syncRunId,
        CancellationToken cancellationToken = default)
    {
        const string sql = """
            SELECT core.requeue_failed_download_queue_items(@job_id, @sync_run_id);
            """;

        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("job_id", jobId);
        command.Parameters.AddWithValue("sync_run_id", syncRunId);

        return await DbCommandLogger.ExecuteScalarAsync<int>(_logger, command, OpRequeueFailedItems, cancellationToken);
    }
}
