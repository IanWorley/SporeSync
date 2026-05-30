-- Auto-queue worker repository functions (see docs/auto-queue-worker-implementation-plan.html Phase 1).

CREATE FUNCTION core.get_due_sftp_sync_jobs()
RETURNS TABLE (
    id uuid,
    connection_profile_id uuid,
    name varchar(200),
    source_path varchar(1000),
    destination_path varchar(1000),
    polling_interval_seconds integer,
    is_enabled boolean,
    last_polled_at timestamptz)
LANGUAGE sql
AS $$
    SELECT j.id,
           j.connection_profile_id,
           j.name,
           j.source_path,
           j.destination_path,
           j.polling_interval_seconds,
           j.is_enabled,
           j.last_polled_at
    FROM core.sftp_sync_jobs j
    WHERE j.is_enabled = true
      AND (
            j.last_polled_at IS NULL
            OR now() >= j.last_polled_at + make_interval(secs => j.polling_interval_seconds)
          )
    ORDER BY j.last_polled_at NULLS FIRST, j.name;
$$;

CREATE FUNCTION core.mark_sftp_sync_job_polled(p_id uuid)
RETURNS TABLE (
    id uuid,
    connection_profile_id uuid,
    name varchar(200),
    source_path varchar(1000),
    destination_path varchar(1000),
    polling_interval_seconds integer,
    is_enabled boolean,
    last_polled_at timestamptz)
LANGUAGE sql
AS $$
    UPDATE core.sftp_sync_jobs AS j
    SET last_polled_at = now(),
        updated_at = now()
    WHERE j.id = p_id
    RETURNING j.id,
              j.connection_profile_id,
              j.name,
              j.source_path,
              j.destination_path,
              j.polling_interval_seconds,
              j.is_enabled,
              j.last_polled_at;
$$;

CREATE FUNCTION core.job_has_active_run(p_job_id uuid)
RETURNS boolean
LANGUAGE sql
AS $$
    SELECT EXISTS (
        SELECT 1
        FROM core.sftp_sync_runs r
        WHERE r.job_id = p_job_id
          AND r.status IN ('queued', 'scanning', 'downloading')
    );
$$;

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

CREATE FUNCTION core.update_download_queue_item_progress(
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

CREATE FUNCTION core.requeue_failed_download_queue_items(
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

CREATE FUNCTION core.run_has_pending_downloads(p_run_id uuid)
RETURNS boolean
LANGUAGE sql
AS $$
    SELECT EXISTS (
        SELECT 1
        FROM core.download_queue_items qi
        WHERE qi.sync_run_id = p_run_id
          AND qi.status IN ('queued', 'comparing', 'downloading')
    );
$$;

CREATE FUNCTION core.recalculate_sftp_sync_run_aggregates(p_run_id uuid)
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
        SELECT count(*) FILTER (WHERE visible_items.status = 'completed')::integer AS completed_file_count,
               count(*) FILTER (WHERE visible_items.status = 'failed')::integer AS failed_file_count,
               count(*) FILTER (WHERE visible_items.status = 'skipped')::integer AS skipped_file_count,
               coalesce(sum(bytes_downloaded), 0)::bigint AS downloaded_bytes,
               max(current_bytes_per_second) AS current_bytes_per_second
        FROM visible_items
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
