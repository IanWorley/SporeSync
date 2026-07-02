CREATE OR REPLACE FUNCTION core.recalculate_sftp_sync_run_aggregates(p_run_id uuid)
RETURNS TABLE (
    id uuid,
    job_id uuid,
    job_name varchar(200),
    status varchar(32),
    started_at timestamptz,
    completed_at timestamptz,
    total_file_count integer,
    completed_file_count integer,
    skipped_file_count integer,
    failed_file_count integer,
    total_bytes bigint,
    downloaded_bytes bigint,
    current_bytes_per_second numeric(20, 2),
    error_message text)
LANGUAGE plpgsql
AS $$
BEGIN
    RETURN QUERY
    WITH visible_items AS (
        SELECT qi.*
        FROM core.download_queue_items qi
        WHERE qi.sync_run_id = p_run_id
          AND (qi.is_group = true OR (qi.is_group = false AND qi.group_remote_path IS NULL))
    ),
    stats AS (
        SELECT count(*) FILTER (WHERE vi.status = 'completed')::integer AS completed_file_count,
               count(*) FILTER (WHERE vi.status = 'failed')::integer AS failed_file_count,
               count(*) FILTER (WHERE vi.status = 'skipped')::integer AS skipped_file_count,
               coalesce(sum(vi.bytes_downloaded), 0)::bigint AS downloaded_bytes,
               max(vi.current_bytes_per_second) AS current_bytes_per_second
        FROM visible_items vi
    ),
    updated AS (
        UPDATE core.sftp_sync_runs AS r
        SET completed_file_count = stats.completed_file_count,
            failed_file_count = stats.failed_file_count,
            skipped_file_count = stats.skipped_file_count,
            downloaded_bytes = stats.downloaded_bytes,
            current_bytes_per_second = stats.current_bytes_per_second
        FROM stats
        WHERE r.id = p_run_id
        RETURNING r.*
    )
    SELECT u.id,
           u.job_id,
           j.name AS job_name,
           u.status,
           u.started_at,
           u.completed_at,
           u.total_file_count,
           u.completed_file_count,
           u.skipped_file_count,
           u.failed_file_count,
           u.total_bytes,
           u.downloaded_bytes,
           u.current_bytes_per_second,
           u.error_message
    FROM updated u
    INNER JOIN core.sftp_sync_jobs j ON j.id = u.job_id;
END;
$$;
