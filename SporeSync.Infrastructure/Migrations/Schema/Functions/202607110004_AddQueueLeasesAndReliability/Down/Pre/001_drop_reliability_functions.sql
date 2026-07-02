-- Drop the reliability helpers and restore the pre-lease function bodies
-- (as created by 202605300001_AddWorkerRepositoryFunctions).

DROP FUNCTION IF EXISTS core.reap_orphaned_sftp_sync_runs(boolean);
DROP FUNCTION IF EXISTS core.requeue_stale_download_queue_items(boolean);
DROP FUNCTION IF EXISTS core.release_download_queue_item(uuid);
DROP FUNCTION IF EXISTS core.renew_download_queue_item_lease(uuid, integer);
DROP FUNCTION IF EXISTS core.renew_sftp_sync_run_lease(uuid, integer);
DROP FUNCTION IF EXISTS core.claim_next_download_queue_item(integer);
DROP FUNCTION IF EXISTS core.update_sftp_sync_run_status(uuid, varchar, integer, bigint, integer, integer, integer, bigint, numeric, text, integer);
DROP FUNCTION IF EXISTS core.create_sftp_sync_run(uuid, integer);

CREATE FUNCTION core.create_sftp_sync_run(p_job_id uuid)
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
DECLARE
    v_run_id uuid := gen_random_uuid();
BEGIN
    INSERT INTO core.sftp_sync_runs (id, job_id, status)
    VALUES (v_run_id, p_job_id, 'queued');

    RETURN QUERY
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
    WHERE r.id = v_run_id;
END;
$$;

CREATE FUNCTION core.update_sftp_sync_run_status(
    p_id uuid,
    p_status varchar(32),
    p_total_file_count integer DEFAULT NULL,
    p_total_bytes bigint DEFAULT NULL,
    p_completed_file_count integer DEFAULT NULL,
    p_skipped_file_count integer DEFAULT NULL,
    p_failed_file_count integer DEFAULT NULL,
    p_downloaded_bytes bigint DEFAULT NULL,
    p_current_bytes_per_second numeric DEFAULT NULL,
    p_error_message text DEFAULT NULL)
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
    WITH updated AS (
        UPDATE core.sftp_sync_runs AS r
        SET status = p_status,
            completed_at = CASE
                WHEN p_status IN ('completed', 'failed', 'cancelled') THEN now()
                ELSE r.completed_at
            END,
            total_file_count = COALESCE(p_total_file_count, r.total_file_count),
            total_bytes = COALESCE(p_total_bytes, r.total_bytes),
            completed_file_count = COALESCE(p_completed_file_count, r.completed_file_count),
            skipped_file_count = COALESCE(p_skipped_file_count, r.skipped_file_count),
            failed_file_count = COALESCE(p_failed_file_count, r.failed_file_count),
            downloaded_bytes = COALESCE(p_downloaded_bytes, r.downloaded_bytes),
            current_bytes_per_second = p_current_bytes_per_second,
            error_message = p_error_message
        WHERE r.id = p_id
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

CREATE FUNCTION core.claim_next_download_queue_item()
RETURNS TABLE (
    id uuid,
    job_id uuid,
    sync_run_id uuid,
    remote_path varchar(2000),
    destination_path varchar(2000),
    file_size_bytes bigint,
    remote_modified_at timestamptz,
    status varchar(32),
    bytes_downloaded bigint,
    current_bytes_per_second numeric(20, 2),
    retry_count integer,
    handled_reason varchar(100),
    error_message text,
    queued_at timestamptz,
    started_at timestamptz,
    completed_at timestamptz,
    updated_at timestamptz,
    is_group boolean,
    group_remote_path varchar(2000),
    child_count integer)
LANGUAGE plpgsql
AS $$
BEGIN
    RETURN QUERY
    WITH next_item AS (
        SELECT qi.id
        FROM core.download_queue_items qi
        WHERE qi.status = 'queued'
          AND (qi.next_attempt_at IS NULL OR qi.next_attempt_at <= now())
          AND (qi.is_group = true OR (qi.is_group = false AND qi.group_remote_path IS NULL))
        ORDER BY qi.queued_at ASC, qi.id
        FOR UPDATE OF qi SKIP LOCKED
        LIMIT 1
    )
    UPDATE core.download_queue_items qi
    SET status = 'downloading',
        started_at = COALESCE(qi.started_at, now()),
        updated_at = now()
    FROM next_item
    WHERE qi.id = next_item.id
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
              qi.child_count;
END;
$$;
