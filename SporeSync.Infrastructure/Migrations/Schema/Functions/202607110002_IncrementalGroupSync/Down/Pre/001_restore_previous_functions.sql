-- Restores the pre-incremental-group-sync versions of the functions
-- (bodies from 202605300001_AddWorkerRepositoryFunctions).

DROP FUNCTION core.upsert_download_queue_item(
    uuid, uuid, varchar, varchar, bigint, timestamptz, boolean, varchar, integer, boolean);

CREATE FUNCTION core.upsert_download_queue_item(
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

DROP FUNCTION core.get_synced_remote_state(uuid);

CREATE FUNCTION core.get_synced_remote_state(p_job_id uuid)
RETURNS TABLE (
    remote_path varchar(2000),
    remote_modified_at timestamptz,
    file_size_bytes bigint,
    status varchar(32))
LANGUAGE sql
AS $$
    SELECT qi.remote_path,
           qi.remote_modified_at,
           qi.file_size_bytes,
           qi.status
    FROM core.download_queue_items qi
    WHERE qi.job_id = p_job_id;
$$;
