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
