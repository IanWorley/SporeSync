-- Run lifecycle controls (cancel/retry) and delete support for jobs/profiles.

CREATE FUNCTION core.cancel_sftp_sync_run(p_run_id uuid)
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
    v_run_id uuid;
BEGIN
    SELECT r.id
    INTO v_run_id
    FROM core.sftp_sync_runs r
    WHERE r.id = p_run_id
      AND r.status IN ('queued', 'scanning', 'downloading')
    FOR UPDATE;

    IF NOT FOUND THEN
        RETURN;
    END IF;

    -- Pending items (including internal group leaves) will never be claimed for
    -- a cancelled run; mark them handled so the run's aggregates settle.
    UPDATE core.download_queue_items qi
    SET status = 'skipped',
        handled_reason = 'run_cancelled',
        current_bytes_per_second = NULL,
        completed_at = now(),
        updated_at = now()
    WHERE qi.sync_run_id = p_run_id
      AND qi.status IN ('queued', 'comparing');

    UPDATE core.sftp_sync_runs r
    SET status = 'cancelled',
        completed_at = now(),
        current_bytes_per_second = NULL
    WHERE r.id = p_run_id;

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
    WHERE r.id = p_run_id;
END;
$$;

-- Advances a run's status during a scan only when the run is still in the
-- expected status. If the run was cancelled mid-scan, the transition is not
-- applied and any items enqueued after the cancellation are skipped so the
-- download worker never claims them.
CREATE FUNCTION core.advance_sftp_sync_run_scan(
    p_run_id uuid,
    p_expected_status varchar(32),
    p_status varchar(32),
    p_total_file_count integer DEFAULT NULL,
    p_total_bytes bigint DEFAULT NULL,
    p_skipped_file_count integer DEFAULT NULL,
    p_error_message text DEFAULT NULL,
    p_lease_seconds integer DEFAULT NULL)
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
    UPDATE core.sftp_sync_runs r
    SET status = p_status,
        completed_at = CASE
            WHEN p_status IN ('completed', 'failed', 'cancelled') THEN now()
            ELSE r.completed_at
        END,
        total_file_count = COALESCE(p_total_file_count, r.total_file_count),
        total_bytes = COALESCE(p_total_bytes, r.total_bytes),
        skipped_file_count = COALESCE(p_skipped_file_count, r.skipped_file_count),
        current_bytes_per_second = NULL,
        error_message = p_error_message,
        lease_expires_at = CASE
            WHEN p_status IN ('completed', 'failed', 'cancelled') THEN NULL
            WHEN p_lease_seconds IS NOT NULL THEN now() + make_interval(secs => p_lease_seconds)
            ELSE r.lease_expires_at
        END
    WHERE r.id = p_run_id
      AND r.status = p_expected_status;

    IF NOT FOUND THEN
        UPDATE core.download_queue_items qi
        SET status = 'skipped',
            handled_reason = 'run_cancelled',
            current_bytes_per_second = NULL,
            completed_at = now(),
            updated_at = now()
        WHERE qi.sync_run_id = p_run_id
          AND qi.status IN ('queued', 'comparing')
          AND EXISTS (
              SELECT 1
              FROM core.sftp_sync_runs r2
              WHERE r2.id = p_run_id
                AND r2.status = 'cancelled');

        IF FOUND THEN
            PERFORM core.recalculate_sftp_sync_run_aggregates(p_run_id);
        END IF;
    END IF;

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
    WHERE r.id = p_run_id;
END;
$$;

CREATE FUNCTION core.retry_failed_download_queue_items(p_run_id uuid)
RETURNS integer
LANGUAGE plpgsql
AS $$
DECLARE
    v_run_id uuid;
    v_retried_count integer;
BEGIN
    SELECT r.id
    INTO v_run_id
    FROM core.sftp_sync_runs r
    WHERE r.id = p_run_id
    FOR UPDATE;

    IF NOT FOUND THEN
        RETURN 0;
    END IF;

    SELECT count(*) FILTER (
        WHERE qi.is_group = true OR qi.group_remote_path IS NULL
    )::integer
    INTO v_retried_count
    FROM core.download_queue_items qi
    WHERE qi.sync_run_id = p_run_id
      AND qi.status = 'failed';

    IF v_retried_count = 0 THEN
        RETURN 0;
    END IF;

    UPDATE core.sftp_sync_runs r
    SET status = 'downloading',
        completed_at = NULL,
        current_bytes_per_second = NULL,
        error_message = NULL
    WHERE r.id = p_run_id;

    UPDATE core.download_queue_items qi
    SET status = 'queued',
        bytes_downloaded = 0,
        current_bytes_per_second = NULL,
        error_message = NULL,
        handled_reason = NULL,
        started_at = NULL,
        completed_at = NULL,
        retry_count = qi.retry_count + 1,
        queued_at = now(),
        updated_at = now()
    WHERE qi.sync_run_id = p_run_id
      AND qi.status = 'failed';

    RETURN v_retried_count;
END;
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
        INNER JOIN core.sftp_sync_runs r ON r.id = qi.sync_run_id
        WHERE qi.status = 'queued'
          AND r.status = 'downloading'
          AND (qi.is_group = true OR (qi.is_group = false AND qi.group_remote_path IS NULL))
        ORDER BY qi.queued_at ASC, qi.id
        FOR UPDATE OF r, qi SKIP LOCKED
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

CREATE FUNCTION core.delete_sftp_sync_job(p_id uuid)
RETURNS boolean
LANGUAGE plpgsql
AS $$
BEGIN
    DELETE FROM core.download_queue_items qi WHERE qi.job_id = p_id;
    DELETE FROM core.sftp_sync_runs r WHERE r.job_id = p_id;
    DELETE FROM core.sftp_sync_jobs j WHERE j.id = p_id;
    RETURN FOUND;
END;
$$;

CREATE FUNCTION core.count_sftp_sync_jobs_for_connection_profile(p_profile_id uuid)
RETURNS integer
LANGUAGE sql
AS $$
    SELECT count(*)::integer
    FROM core.sftp_sync_jobs j
    WHERE j.connection_profile_id = p_profile_id;
$$;

CREATE FUNCTION core.delete_sftp_connection_profile(p_id uuid)
RETURNS boolean
LANGUAGE plpgsql
AS $$
BEGIN
    DELETE FROM core.sftp_connection_profiles p WHERE p.id = p_id;
    RETURN FOUND;
END;
$$;
