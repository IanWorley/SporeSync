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

        // Phase 6 (plan:372 + rules.md:24-48 Concrete Example + M2):
        // Seed the exact first-child opaque grouping tree for dev demo.
        // Visible: 3 rows (reports/ group, customers.csv loose, archive/ group).
        // Internal leaves linked via group_remote_path; aggregates on group rows.
        // 1 failed group pre-seeded for requeue demo. Run totals use visible count + physical bytes (hybrid).
        // Old flat 6-row seed preserved below as commented alternative for regression.
        const long q1Sales = 45_000_000;
        const long summaryXlsx = 25_000_000;
        const long forecastCsv = 10_000_000;
        const long reportsBytes = q1Sales + summaryXlsx + forecastCsv; // 80M
        const long customersCsv = 74_000_000;
        const long backupZip = 50_000_000;
        const long archiveBytes = backupZip; // 50M
        const long totalBytes = reportsBytes + customersCsv + archiveBytes; // 204M exactly

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
                3,   -- visible first-child only (rules.md:153 + invariant #6 + Phase 3 recalc)
                1,   -- customers.csv
                0,
                1,   -- archive/ pre-failed for requeue demo
                @total_bytes,
                @initial_downloaded,
                1450000)
            ON CONFLICT (id)
            DO UPDATE SET
                status = 'downloading',
                completed_at = NULL,
                total_file_count = 3,
                completed_file_count = 1,
                skipped_file_count = 0,
                failed_file_count = 1,
                total_bytes = @total_bytes,
                downloaded_bytes = @initial_downloaded,
                current_bytes_per_second = 1450000;

            DELETE FROM core.download_queue_items
            WHERE sync_run_id = @run_id;

            INSERT INTO core.download_queue_items (
                id, job_id, sync_run_id, remote_path, destination_path, file_size_bytes, remote_modified_at,
                status, bytes_downloaded, current_bytes_per_second, retry_count, handled_reason, error_message,
                queued_at, started_at, completed_at,
                is_group, group_remote_path, child_count)
            VALUES
                -- reports/ (visible opaque group)
                ('40000000-0000-0000-0000-0000000000a1', @job_id, @run_id,
                 '/remote/incoming/reports/', '/data/incoming/reports/',
                 @reports_bytes, now() - interval '1 hour',
                 'downloading', 22500000, 1450000, 0, NULL, NULL,
                 now() - interval '3 minutes', now() - interval '2 minutes 50 seconds', NULL,
                 true, NULL, 3),

                -- reports/ leaves (internal, linked)
                ('40000000-0000-0000-0000-0000000000a2', @job_id, @run_id,
                 '/remote/incoming/reports/2026/Q1-sales.pdf', '/data/incoming/reports/2026/Q1-sales.pdf',
                 @q1_sales, now() - interval '1 hour',
                 'downloading', 22500000, 1450000, 0, NULL, NULL,
                 now() - interval '2 minutes 55 seconds', now() - interval '2 minutes 50 seconds', NULL,
                 false, '/remote/incoming/reports/', 0),
                ('40000000-0000-0000-0000-0000000000a3', @job_id, @run_id,
                 '/remote/incoming/reports/summary.xlsx', '/data/incoming/reports/summary.xlsx',
                 @summary_xlsx, now() - interval '55 minutes',
                 'queued', 0, NULL, 0, NULL, NULL,
                 now() - interval '2 minutes 50 seconds', NULL, NULL,
                 false, '/remote/incoming/reports/', 0),
                ('40000000-0000-0000-0000-0000000000a4', @job_id, @run_id,
                 '/remote/incoming/reports/2026/Q1-forecast.csv', '/data/incoming/reports/2026/Q1-forecast.csv',
                 @forecast_csv, now() - interval '50 minutes',
                 'queued', 0, NULL, 0, NULL, NULL,
                 now() - interval '2 minutes 45 seconds', NULL, NULL,
                 false, '/remote/incoming/reports/', 0),

                -- customers.csv (visible loose file)
                ('40000000-0000-0000-0000-0000000000a5', @job_id, @run_id,
                 '/remote/incoming/customers.csv', '/data/incoming/customers.csv',
                 @customers_csv, now() - interval '45 minutes',
                 'completed', @customers_csv, NULL, 0, NULL, NULL,
                 now() - interval '3 minutes', now() - interval '2 minutes 55 seconds', now() - interval '2 minutes 30 seconds',
                 false, NULL, 0),

                -- archive/ (visible opaque group, pre-failed for requeue demo)
                ('40000000-0000-0000-0000-0000000000a6', @job_id, @run_id,
                 '/remote/incoming/archive/', '/data/incoming/archive/',
                 @archive_bytes, now() - interval '40 minutes',
                 'failed', 0, NULL, 0, NULL, 'Simulated subtree failure for requeue demo (Phase 6)',
                 now() - interval '2 minutes 40 seconds', now() - interval '2 minutes 30 seconds', now() - interval '2 minutes 20 seconds',
                 true, NULL, 1),

                -- archive/ leaf (internal)
                ('40000000-0000-0000-0000-0000000000a7', @job_id, @run_id,
                 '/remote/incoming/archive/old/backup.zip', '/data/incoming/archive/old/backup.zip',
                 @backup_zip, now() - interval '35 minutes',
                 'failed', 0, NULL, 0, NULL, NULL,
                 now() - interval '2 minutes 35 seconds', now() - interval '2 minutes 25 seconds', now() - interval '2 minutes 15 seconds',
                 false, '/remote/incoming/archive/', 0);
            """;

        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("profile_id", profileId);
        command.Parameters.AddWithValue("job_id", jobId);
        command.Parameters.AddWithValue("run_id", runId);
        command.Parameters.AddWithValue("reports_bytes", reportsBytes);
        command.Parameters.AddWithValue("q1_sales", q1Sales);
        command.Parameters.AddWithValue("summary_xlsx", summaryXlsx);
        command.Parameters.AddWithValue("forecast_csv", forecastCsv);
        command.Parameters.AddWithValue("customers_csv", customersCsv);
        command.Parameters.AddWithValue("archive_bytes", archiveBytes);
        command.Parameters.AddWithValue("backup_zip", backupZip);
        command.Parameters.AddWithValue("total_bytes", totalBytes);
        command.Parameters.AddWithValue("initial_downloaded", customersCsv + 22_500_000L);
        await command.ExecuteNonQueryAsync(cancellationToken);

        return runId;

        /*
        // Pre-Phase 6 flat-only seed (6 visible rows, no groups) — kept for regression testing flat behavior.
        // DELETE + INSERT block identical to the original implementation before Phase 6.
        */
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

    // Phase 6 additions (plan:373 + 374 + Phase 5 hybrid semantics + rules.md:126/129-134).
    // Hybrid aggregate-on-group-row: when leaves advance, we mirror exact SUMs/status to the visible group row.
    // Picker (AdvanceQueueItemAsync) is intentionally left byte-identical for flat compat.

    private async Task AdvanceAsync(CancellationToken cancellationToken)
    {
        var item = await AdvanceQueueItemAsync(cancellationToken);
        if (item is null)
        {
            return;
        }

        // Phase 6: if an internal leaf was advanced, mirror its group aggregates and broadcast the *visible* group row instead.
        if (!item.IsGroup && item.GroupRemotePath is not null && item.SyncRunId.HasValue)
        {
            await SyncGroupAggregatesFromLeavesAsync(item.GroupRemotePath, item.SyncRunId.Value, cancellationToken);
            var visibleGroup = await LoadQueueItemByRemotePathAsync(item.GroupRemotePath, item.SyncRunId.Value, cancellationToken);
            if (visibleGroup is not null)
            {
                item = visibleGroup;
            }
        }

        var run = await RecalculateRunAsync(item.SyncRunId!.Value, cancellationToken);
        await _broadcaster.QueueItemUpdatedAsync(item, cancellationToken);
        await _broadcaster.RunUpdatedAsync(run, cancellationToken);
    }

    private async Task SyncGroupAggregatesFromLeavesAsync(string groupRemotePath, Guid runId, CancellationToken ct)
    {
        const string sql = """
            WITH leaf_stats AS (
                SELECT
                    COALESCE(SUM(bytes_downloaded), 0) AS bytes_downloaded,
                    COALESCE(SUM(current_bytes_per_second), 0) AS current_bps,
                    COUNT(*) FILTER (WHERE status IN ('completed','skipped','failed')) AS done_count,
                    COUNT(*) AS leaf_count,
                    BOOL_OR(status = 'downloading') AS any_downloading,
                    BOOL_OR(status = 'failed') AS any_failed
                FROM core.download_queue_items
                WHERE sync_run_id = @run_id AND group_remote_path = @grp AND is_group = false
            )
            UPDATE core.download_queue_items g
            SET bytes_downloaded = ls.bytes_downloaded,
                current_bytes_per_second = NULLIF(ls.current_bps, 0),
                status = CASE
                    WHEN ls.any_failed THEN 'failed'
                    WHEN ls.done_count = ls.leaf_count THEN 'completed'
                    WHEN ls.any_downloading OR ls.bytes_downloaded > 0 THEN 'downloading'
                    ELSE 'queued'
                END,
                started_at = COALESCE(g.started_at, now()),
                completed_at = CASE WHEN ls.done_count = ls.leaf_count AND NOT ls.any_failed THEN now() ELSE NULL END,
                updated_at = now()
            FROM leaf_stats ls
            WHERE g.sync_run_id = @run_id AND g.remote_path = @grp AND g.is_group = true;
            """;

        await using var conn = await _dataSource.OpenConnectionAsync(ct);
        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("run_id", runId);
        cmd.Parameters.AddWithValue("grp", groupRemotePath);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    private async Task<DownloadQueueItemResponse?> LoadQueueItemByRemotePathAsync(string remotePath, Guid runId, CancellationToken ct)
    {
        const string sql = """
            SELECT id, job_id, sync_run_id, remote_path, destination_path, file_size_bytes, remote_modified_at,
                   status, bytes_downloaded, current_bytes_per_second, retry_count, handled_reason, error_message,
                   queued_at, started_at, completed_at, updated_at, is_group, group_remote_path, child_count
            FROM core.download_queue_items
            WHERE sync_run_id = @run_id AND remote_path = @path
            LIMIT 1;
            """;

        await using var conn = await _dataSource.OpenConnectionAsync(ct);
        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("run_id", runId);
        cmd.Parameters.AddWithValue("path", remotePath);
        await using var r = await cmd.ExecuteReaderAsync(ct);
        if (!await r.ReadAsync(ct)) return null;
        return ReadQueueItem(r);
    }

    public async Task RequeueGroupAsync(Guid queueItemId, CancellationToken cancellationToken = default)
    {
        // Exact subtree requeue per Phase 5 semantics + rules.md:129-134 (group row + all leaves via group_remote_path).
        const string sql = """
            UPDATE core.download_queue_items
            SET status = 'queued',
                bytes_downloaded = 0,
                current_bytes_per_second = NULL,
                error_message = NULL,
                handled_reason = NULL,
                started_at = NULL,
                completed_at = NULL,
                retry_count = 0,
                updated_at = now()
            WHERE id = @id AND is_group = true;

            UPDATE core.download_queue_items l
            SET status = 'queued',
                bytes_downloaded = 0,
                current_bytes_per_second = NULL,
                error_message = NULL,
                handled_reason = NULL,
                started_at = NULL,
                completed_at = NULL,
                retry_count = 0,
                updated_at = now()
            WHERE l.sync_run_id = (SELECT sync_run_id FROM core.download_queue_items WHERE id = @id)
              AND l.group_remote_path = (SELECT remote_path FROM core.download_queue_items WHERE id = @id)
              AND l.is_group = false;
            """;

        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("id", queueItemId);
        await command.ExecuteNonQueryAsync(cancellationToken);

        // Immediate demo feedback: broadcast the now-queued visible group + updated run.
        var group = await LoadQueueItemByIdAsync(queueItemId, cancellationToken);
        if (group?.SyncRunId != null)
        {
            var run = await RecalculateRunAsync(group.SyncRunId.Value, cancellationToken);
            await _broadcaster.QueueItemUpdatedAsync(group, cancellationToken);
            await _broadcaster.RunUpdatedAsync(run, cancellationToken);
        }
    }

    private async Task<DownloadQueueItemResponse?> LoadQueueItemByIdAsync(Guid id, CancellationToken ct)
    {
        const string sql = """
            SELECT id, job_id, sync_run_id, remote_path, destination_path, file_size_bytes, remote_modified_at,
                   status, bytes_downloaded, current_bytes_per_second, retry_count, handled_reason, error_message,
                   queued_at, started_at, completed_at, updated_at, is_group, group_remote_path, child_count
            FROM core.download_queue_items WHERE id = @id;
            """;

        await using var conn = await _dataSource.OpenConnectionAsync(ct);
        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("id", id);
        await using var r = await cmd.ExecuteReaderAsync(ct);
        if (!await r.ReadAsync(ct)) return null;
        return ReadQueueItem(r);
    }
}
