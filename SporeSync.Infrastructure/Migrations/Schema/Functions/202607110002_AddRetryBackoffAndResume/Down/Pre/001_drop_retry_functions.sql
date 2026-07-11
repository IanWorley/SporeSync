-- Drop the retry/backoff/dead-letter helpers and restore the pre-retry function bodies
-- (originals from 202605300001_AddWorkerRepositoryFunctions) before the next_attempt_at
-- column is dropped by the schema down script.

DROP FUNCTION IF EXISTS core.record_download_queue_item_failure(uuid, text, integer, timestamptz, bigint);
DROP FUNCTION IF EXISTS core.defer_download_queue_item(uuid, timestamptz, varchar, bigint);
DROP FUNCTION IF EXISTS core.retry_download_queue_item(uuid);

CREATE OR REPLACE FUNCTION core.claim_next_download_queue_item()
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

CREATE OR REPLACE FUNCTION core.upsert_download_queue_item(
    p_job_id uuid,
    p_sync_run_id uuid,
    p_remote_path varchar(2000),
    p_destination_path varchar(2000),
    p_file_size_bytes bigint,
    p_remote_modified_at timestamptz,
    p_is_group boolean,
    p_group_remote_path varchar(2000),
    p_child_count integer)
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
LANGUAGE sql
AS $$
    INSERT INTO core.download_queue_items AS qi (
        id,
        job_id,
        sync_run_id,
        remote_path,
        destination_path,
        file_size_bytes,
        remote_modified_at,
        status,
        bytes_downloaded,
        is_group,
        group_remote_path,
        child_count)
    VALUES (
        gen_random_uuid(),
        p_job_id,
        p_sync_run_id,
        p_remote_path,
        p_destination_path,
        p_file_size_bytes,
        p_remote_modified_at,
        'queued',
        0,
        p_is_group,
        p_group_remote_path,
        p_child_count)
    ON CONFLICT (job_id, remote_path)
    DO UPDATE SET
        sync_run_id = EXCLUDED.sync_run_id,
        destination_path = EXCLUDED.destination_path,
        file_size_bytes = EXCLUDED.file_size_bytes,
        remote_modified_at = EXCLUDED.remote_modified_at,
        status = 'queued',
        bytes_downloaded = 0,
        current_bytes_per_second = NULL,
        error_message = NULL,
        handled_reason = NULL,
        started_at = NULL,
        completed_at = NULL,
        is_group = EXCLUDED.is_group,
        group_remote_path = EXCLUDED.group_remote_path,
        child_count = EXCLUDED.child_count,
        queued_at = now(),
        updated_at = now()
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
$$;

CREATE OR REPLACE FUNCTION core.update_download_queue_item_progress(
    p_id uuid,
    p_status varchar(32),
    p_bytes_downloaded bigint,
    p_current_bytes_per_second numeric,
    p_error_message text,
    p_handled_reason varchar(100))
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
LANGUAGE sql
AS $$
    UPDATE core.download_queue_items AS qi
    SET status = p_status,
        bytes_downloaded = p_bytes_downloaded,
        current_bytes_per_second = p_current_bytes_per_second,
        error_message = p_error_message,
        handled_reason = p_handled_reason,
        completed_at = CASE
            WHEN p_status IN ('completed', 'failed', 'skipped') THEN now()
            ELSE qi.completed_at
        END,
        updated_at = now()
    WHERE qi.id = p_id
      AND (
          p_status IN ('completed', 'failed', 'skipped')
          OR qi.status NOT IN ('completed', 'failed', 'skipped')
      )
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
$$;

CREATE OR REPLACE FUNCTION core.requeue_failed_download_queue_items(
    p_job_id uuid,
    p_sync_run_id uuid)
RETURNS integer
LANGUAGE plpgsql
AS $$
DECLARE
    v_count integer;
BEGIN
    UPDATE core.download_queue_items qi
    SET sync_run_id = p_sync_run_id,
        status = 'queued',
        bytes_downloaded = 0,
        current_bytes_per_second = NULL,
        error_message = NULL,
        handled_reason = NULL,
        started_at = NULL,
        completed_at = NULL,
        queued_at = now(),
        updated_at = now()
    WHERE qi.job_id = p_job_id
      AND qi.status = 'failed';

    GET DIAGNOSTICS v_count = ROW_COUNT;
    RETURN v_count;
END;
$$;
