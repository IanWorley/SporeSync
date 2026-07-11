-- Incremental grouped-directory sync support.
--
-- 1) core.upsert_download_queue_item gains p_preserve_completed: when true and the existing
--    row is already 'completed', the row is moved into the new sync run (sync_run_id,
--    destination/size/mtime/group metadata refreshed) while keeping its completed status,
--    downloaded bytes, and timestamps. The scan orchestrator uses this to carry unchanged
--    group leaves forward so a changed group only re-downloads the leaves that actually changed.
--
-- 2) core.get_synced_remote_state additionally returns child_count so change detection can
--    notice group membership changes (e.g. a leaf removed remotely) even when the lossy
--    byte-sum/max-mtime fingerprint happens to collide.

DROP FUNCTION core.upsert_download_queue_item(
    uuid, uuid, varchar, varchar, bigint, timestamptz, boolean, varchar, integer);

CREATE FUNCTION core.upsert_download_queue_item(
    p_job_id uuid,
    p_sync_run_id uuid,
    p_remote_path varchar(2000),
    p_destination_path varchar(2000),
    p_file_size_bytes bigint,
    p_remote_modified_at timestamptz,
    p_is_group boolean,
    p_group_remote_path varchar(2000),
    p_child_count integer,
    p_preserve_completed boolean DEFAULT false)
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
        status = CASE
            WHEN p_preserve_completed AND qi.status = 'completed' THEN qi.status
            ELSE 'queued'
        END,
        bytes_downloaded = CASE
            WHEN p_preserve_completed AND qi.status = 'completed' THEN qi.bytes_downloaded
            ELSE 0
        END,
        current_bytes_per_second = NULL,
        error_message = NULL,
        handled_reason = NULL,
        retry_count = CASE
            WHEN p_preserve_completed AND qi.status = 'completed' THEN qi.retry_count
            ELSE 0
        END,
        next_attempt_at = CASE
            WHEN p_preserve_completed AND qi.status = 'completed' THEN qi.next_attempt_at
            ELSE NULL
        END,
        started_at = CASE
            WHEN p_preserve_completed AND qi.status = 'completed' THEN qi.started_at
            ELSE NULL
        END,
        completed_at = CASE
            WHEN p_preserve_completed AND qi.status = 'completed' THEN qi.completed_at
            ELSE NULL
        END,
        is_group = EXCLUDED.is_group,
        group_remote_path = EXCLUDED.group_remote_path,
        child_count = EXCLUDED.child_count,
        queued_at = CASE
            WHEN p_preserve_completed AND qi.status = 'completed' THEN qi.queued_at
            ELSE now()
        END,
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

DROP FUNCTION core.get_synced_remote_state(uuid);

CREATE FUNCTION core.get_synced_remote_state(p_job_id uuid)
RETURNS TABLE (
    remote_path varchar(2000),
    remote_modified_at timestamptz,
    file_size_bytes bigint,
    status varchar(32),
    child_count integer)
LANGUAGE sql
AS $$
    SELECT qi.remote_path,
           qi.remote_modified_at,
           qi.file_size_bytes,
           qi.status,
           qi.child_count
    FROM core.download_queue_items qi
    WHERE qi.job_id = p_job_id;
$$;
