using Npgsql;
using SftpSync.Web.DTO;
using SftpSync.Web.Hubs;

namespace SftpSync.Web.Development;

public sealed class DevelopmentSimulationService : BackgroundService
{
    private readonly NpgsqlDataSource _dataSource;
    private readonly IDashboardBroadcaster _broadcaster;
    private readonly ILogger<DevelopmentSimulationService> _logger;
    private volatile bool _isRunning;

    public DevelopmentSimulationService(
        NpgsqlDataSource dataSource,
        IDashboardBroadcaster broadcaster,
        ILogger<DevelopmentSimulationService> logger)
    {
        _dataSource = dataSource;
        _broadcaster = broadcaster;
        _logger = logger;
    }

    public bool IsRunning => _isRunning;

    public async Task<Guid> SeedAsync(CancellationToken cancellationToken = default)
    {
        var profileId = Guid.Parse("10000000-0000-0000-0000-000000000001");
        var jobId = Guid.Parse("20000000-0000-0000-0000-000000000001");
        var runId = Guid.Parse("30000000-0000-0000-0000-000000000001");

        const string sql = """
            INSERT INTO core.sftp_connection_profiles (
                id,
                name,
                host,
                port,
                username,
                encrypted_password,
                is_default)
            VALUES (
                @profile_id,
                'Development SFTP',
                'sftp.dev.local',
                22,
                'sync-user',
                'development-secret',
                false)
            ON CONFLICT (id)
            DO UPDATE SET
                name = EXCLUDED.name,
                host = EXCLUDED.host,
                username = EXCLUDED.username,
                encrypted_password = EXCLUDED.encrypted_password,
                updated_at = now();

            INSERT INTO core.sftp_sync_jobs (
                id,
                connection_profile_id,
                name,
                source_path,
                destination_path,
                polling_interval_seconds,
                is_enabled)
            VALUES (
                @job_id,
                @profile_id,
                'Development Import',
                '/remote/incoming',
                '/data/incoming',
                120,
                true)
            ON CONFLICT (id)
            DO UPDATE SET
                name = EXCLUDED.name,
                source_path = EXCLUDED.source_path,
                destination_path = EXCLUDED.destination_path,
                is_enabled = EXCLUDED.is_enabled,
                updated_at = now();

            INSERT INTO core.sftp_sync_runs (
                id,
                job_id,
                status,
                started_at,
                total_file_count,
                completed_file_count,
                skipped_file_count,
                failed_file_count,
                total_bytes,
                downloaded_bytes,
                current_bytes_per_second)
            VALUES (
                @run_id,
                @job_id,
                'downloading',
                now() - interval '2 minutes',
                6,
                1,
                1,
                0,
                204000000,
                48000000,
                1450000)
            ON CONFLICT (id)
            DO UPDATE SET
                status = 'downloading',
                completed_at = NULL,
                total_file_count = 6,
                completed_file_count = 1,
                skipped_file_count = 1,
                failed_file_count = 0,
                total_bytes = 204000000,
                downloaded_bytes = 48000000,
                current_bytes_per_second = 1450000;

            DELETE FROM core.download_queue_items
            WHERE sync_run_id = @run_id;

            INSERT INTO core.download_queue_items (
                id,
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
                queued_at,
                started_at,
                completed_at)
            VALUES
                ('40000000-0000-0000-0000-000000000001', @job_id, @run_id, '/remote/incoming/2026-05-27/customers.csv', '/data/incoming/customers.csv', 12000000, now() - interval '1 hour', 'completed', 12000000, NULL, 0, NULL, now() - interval '3 minutes', now() - interval '2 minutes 50 seconds', now() - interval '2 minutes'),
                ('40000000-0000-0000-0000-000000000002', @job_id, @run_id, '/remote/incoming/2026-05-27/orders.csv', '/data/incoming/orders.csv', 74000000, now() - interval '50 minutes', 'downloading', 36000000, 1450000, 0, NULL, now() - interval '2 minutes 45 seconds', now() - interval '90 seconds', NULL),
                ('40000000-0000-0000-0000-000000000003', @job_id, @run_id, '/remote/incoming/2026-05-27/products.csv', '/data/incoming/products.csv', 18000000, now() - interval '45 minutes', 'queued', 0, NULL, 0, NULL, now() - interval '2 minutes 40 seconds', NULL, NULL),
                ('40000000-0000-0000-0000-000000000004', @job_id, @run_id, '/remote/incoming/archive/images.zip', '/data/incoming/images.zip', 88000000, now() - interval '30 minutes', 'queued', 0, NULL, 0, NULL, now() - interval '2 minutes 30 seconds', NULL, NULL),
                ('40000000-0000-0000-0000-000000000005', @job_id, @run_id, '/remote/incoming/2026-05-27/readme.txt', '/data/incoming/readme.txt', 2000000, now() - interval '20 minutes', 'skipped', 0, NULL, 0, 'unchanged', now() - interval '2 minutes 20 seconds', NULL, now() - interval '2 minutes 10 seconds'),
                ('40000000-0000-0000-0000-000000000006', @job_id, @run_id, '/remote/incoming/2026-05-27/returns.csv', '/data/incoming/returns.csv', 10000000, now() - interval '10 minutes', 'queued', 0, NULL, 0, NULL, now() - interval '2 minutes 5 seconds', NULL, NULL);
            """;

        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("profile_id", profileId);
        command.Parameters.AddWithValue("job_id", jobId);
        command.Parameters.AddWithValue("run_id", runId);
        await command.ExecuteNonQueryAsync(cancellationToken);

        return runId;
    }

    public void StartSimulation()
    {
        _isRunning = true;
    }

    public void StopSimulation()
    {
        _isRunning = false;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(400));

        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            if (!_isRunning)
            {
                continue;
            }

            try
            {
                await AdvanceAsync(stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(ex, "Development simulation tick failed.");
            }
        }
    }

    private async Task AdvanceAsync(CancellationToken cancellationToken)
    {
        var item = await AdvanceQueueItemAsync(cancellationToken);
        if (item is null)
        {
            return;
        }

        var run = await RecalculateRunAsync(item.SyncRunId!.Value, cancellationToken);
        await _broadcaster.QueueItemUpdatedAsync(item, cancellationToken);
        await _broadcaster.RunUpdatedAsync(run, cancellationToken);
    }

    private async Task<DownloadQueueItemResponse?> AdvanceQueueItemAsync(CancellationToken cancellationToken)
    {
        const string sql = """
            WITH active_item AS (
                SELECT id
                FROM core.download_queue_items
                WHERE sync_run_id = @run_id
                  AND status IN ('queued', 'downloading')
                ORDER BY
                    CASE status WHEN 'downloading' THEN 0 ELSE 1 END,
                    queued_at,
                    id
                LIMIT 1
            ),
            updated_item AS (
                UPDATE core.download_queue_items qi
                SET status = CASE
                        WHEN LEAST(qi.file_size_bytes, qi.bytes_downloaded + 3500000) >= qi.file_size_bytes THEN 'completed'
                        ELSE 'downloading'
                    END,
                    bytes_downloaded = LEAST(qi.file_size_bytes, qi.bytes_downloaded + 3500000),
                    current_bytes_per_second = CASE
                        WHEN LEAST(qi.file_size_bytes, qi.bytes_downloaded + 3500000) >= qi.file_size_bytes THEN NULL
                        ELSE 3500000
                    END,
                    started_at = COALESCE(qi.started_at, now()),
                    completed_at = CASE
                        WHEN LEAST(qi.file_size_bytes, qi.bytes_downloaded + 3500000) >= qi.file_size_bytes THEN now()
                        ELSE NULL
                    END,
                    updated_at = now()
                FROM active_item
                WHERE qi.id = active_item.id
                RETURNING qi.id,
                          qi.job_id,
                          qi.sync_run_id,
                          qi.remote_path,
                          qi.destination_path,
                          qi.file_size_bytes,
                          qi.remote_modified_at,
                          qi.status,
                          qi.bytes_downloaded,
                          qi.current_bytes_per_second,
                          qi.retry_count,
                          qi.handled_reason,
                          qi.error_message,
                          qi.queued_at,
                          qi.started_at,
                          qi.completed_at,
                          qi.updated_at,
                          qi.is_group,
                          qi.group_remote_path,
                          qi.child_count
            )
            SELECT *
            FROM updated_item;
            """;

        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("run_id", Guid.Parse("30000000-0000-0000-0000-000000000001"));

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        return ReadQueueItem(reader);
    }

    private async Task<SftpSyncRunResponse> RecalculateRunAsync(
        Guid runId,
        CancellationToken cancellationToken)
    {
        const string sql = """
            WITH totals AS (
                -- Phase 3 (plan:342): logical *FileCount fields = number of visible first-child entries only
                -- (is_group=true OR (is_group=false AND group_remote_path IS NULL)).
                -- Per grouping-rules.md:146 (at enqueue = visible first-child cardinality, not SUM(child_count))
                -- and invariant #6, plus locked decision #3 (bytes primary, counts secondary).
                -- Physical byte sums remain over all rows (hybrid leaves included).
                -- On current flat-only data this is a no-op (all rows visible).
                SELECT count(*) FILTER (WHERE (is_group = true OR (is_group = false AND group_remote_path IS NULL)))::int AS total_file_count,
                       count(*) FILTER (WHERE (is_group = true OR (is_group = false AND group_remote_path IS NULL)) AND status = 'completed')::int AS completed_file_count,
                       count(*) FILTER (WHERE (is_group = true OR (is_group = false AND group_remote_path IS NULL)) AND status = 'skipped')::int AS skipped_file_count,
                       count(*) FILTER (WHERE (is_group = true OR (is_group = false AND group_remote_path IS NULL)) AND status = 'failed')::int AS failed_file_count,
                       coalesce(sum(file_size_bytes), 0)::bigint AS total_bytes,
                       coalesce(sum(bytes_downloaded), 0)::bigint AS downloaded_bytes,
                       coalesce(sum(current_bytes_per_second), 0)::numeric(20, 2) AS current_bytes_per_second
                FROM core.download_queue_items
                WHERE sync_run_id = @run_id
            ),
            updated_run AS (
                UPDATE core.sftp_sync_runs r
                SET status = CASE
                        WHEN totals.total_file_count = totals.completed_file_count + totals.skipped_file_count + totals.failed_file_count THEN 'completed'
                        ELSE 'downloading'
                    END,
                    completed_at = CASE
                        WHEN totals.total_file_count = totals.completed_file_count + totals.skipped_file_count + totals.failed_file_count THEN now()
                        ELSE NULL
                    END,
                    total_file_count = totals.total_file_count,
                    completed_file_count = totals.completed_file_count,
                    skipped_file_count = totals.skipped_file_count,
                    failed_file_count = totals.failed_file_count,
                    total_bytes = totals.total_bytes,
                    downloaded_bytes = totals.downloaded_bytes,
                    current_bytes_per_second = NULLIF(totals.current_bytes_per_second, 0)
                FROM totals
                WHERE r.id = @run_id
                RETURNING r.id,
                          r.job_id,
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
            )
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
            FROM updated_run r
            INNER JOIN core.sftp_sync_jobs j ON j.id = r.job_id;
            """;

        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("run_id", runId);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            throw new InvalidOperationException("Development simulation run was not found.");
        }

        return ReadRun(reader);
    }

    private static SftpSyncRunResponse ReadRun(NpgsqlDataReader reader)
    {
        return new SftpSyncRunResponse(
            reader.GetGuid(0),
            reader.GetGuid(1),
            reader.GetString(2),
            reader.GetString(3),
            reader.GetFieldValue<DateTimeOffset>(4),
            reader.IsDBNull(5) ? null : reader.GetFieldValue<DateTimeOffset>(5),
            reader.GetInt32(6),
            reader.GetInt32(7),
            reader.GetInt32(8),
            reader.GetInt32(9),
            reader.GetInt64(10),
            reader.GetInt64(11),
            reader.IsDBNull(12) ? null : reader.GetDecimal(12),
            reader.IsDBNull(13) ? null : reader.GetString(13));
    }

    private static DownloadQueueItemResponse ReadQueueItem(NpgsqlDataReader reader)
    {
        return new DownloadQueueItemResponse(
            reader.GetGuid(0),
            reader.GetGuid(1),
            reader.IsDBNull(2) ? null : reader.GetGuid(2),
            reader.GetString(3),
            reader.GetString(4),
            reader.GetInt64(5),
            reader.IsDBNull(6) ? null : reader.GetFieldValue<DateTimeOffset>(6),
            reader.GetString(7),
            reader.GetInt64(8),
            reader.IsDBNull(9) ? null : reader.GetDecimal(9),
            reader.GetInt32(10),
            reader.IsDBNull(11) ? null : reader.GetString(11),
            reader.IsDBNull(12) ? null : reader.GetString(12),
            reader.GetFieldValue<DateTimeOffset>(13),
            reader.IsDBNull(14) ? null : reader.GetFieldValue<DateTimeOffset>(14),
            reader.IsDBNull(15) ? null :             reader.GetFieldValue<DateTimeOffset>(15),
            reader.GetFieldValue<DateTimeOffset>(16),
            reader.GetBoolean(17),
            reader.IsDBNull(18) ? null : reader.GetString(18),
            reader.GetInt32(19));
    }
}
