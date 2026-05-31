CREATE FUNCTION core.mark_remote_deleted_download_queue_items(
    p_job_id uuid,
    p_sync_run_id uuid,
    p_remote_paths text[])
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
    WITH updated AS (
        UPDATE core.download_queue_items AS qi
        SET sync_run_id = p_sync_run_id,
            status = 'skipped',
            bytes_downloaded = 0,
            current_bytes_per_second = NULL,
            handled_reason = 'remote_deleted',
            error_message = NULL,
            started_at = NULL,
            completed_at = now(),
            updated_at = now()
        WHERE qi.job_id = p_job_id
          AND qi.remote_path = ANY(p_remote_paths)
          AND qi.status = 'completed'
        RETURNING qi.*
    )
    SELECT u.id,
           u.job_id,
           u.sync_run_id,
           u.remote_path,
           u.destination_path,
           u.file_size_bytes,
           u.remote_modified_at,
           u.status,
           u.bytes_downloaded,
           u.current_bytes_per_second,
           u.retry_count,
           u.handled_reason,
           u.error_message,
           u.queued_at,
           u.started_at,
           u.completed_at,
           u.updated_at,
           u.is_group,
           u.group_remote_path,
           u.child_count
    FROM updated u
    WHERE u.is_group = true
       OR (u.is_group = false AND u.group_remote_path IS NULL);
$$;
