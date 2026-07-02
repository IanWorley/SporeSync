-- Reliability follow-ups:
-- 1. Crash recovery & leasing: claimed queue items carry a lease; expired leases are
--    requeued and orphaned runs are reaped by the recovery sweep.
-- 2. Enqueue/claim race: items are only claimable once their run reaches the
--    'downloading' status (i.e. after scan/enqueue finished), and run creation is
--    atomic via the ux_sftp_sync_runs_active_job partial unique index.

-- ---------------------------------------------------------------------------
-- Atomic run creation. Replaces core.create_sftp_sync_run(uuid): returns zero
-- rows when the job already has an active run instead of racing the
-- check-then-insert done by callers.
-- ---------------------------------------------------------------------------
DROP FUNCTION IF EXISTS core.create_sftp_sync_run(uuid);

CREATE FUNCTION core.create_sftp_sync_run(
    p_job_id uuid,
    p_lease_seconds integer)
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
-- The ON CONFLICT target column would otherwise be ambiguous with the job_id
-- OUT parameter of RETURNS TABLE.
#variable_conflict use_column
DECLARE
    v_run_id uuid;
BEGIN
    INSERT INTO core.sftp_sync_runs AS r (id, job_id, status, lease_expires_at)
    VALUES (
        gen_random_uuid(),
        p_job_id,
        'queued',
        now() + make_interval(secs => p_lease_seconds))
    ON CONFLICT (job_id) WHERE r.status IN ('queued', 'scanning', 'downloading')
    DO NOTHING
    RETURNING r.id INTO v_run_id;

    IF v_run_id IS NULL THEN
        RETURN;
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
    WHERE r.id = v_run_id;
END;
$$;

-- ---------------------------------------------------------------------------
-- Guarded run status update. Replaces the previous signature:
-- * terminal runs (completed/failed/cancelled) are never resurrected — the
--   current row is returned unchanged instead;
-- * active statuses can renew the run lease, terminal statuses clear it.
-- ---------------------------------------------------------------------------
DROP FUNCTION IF EXISTS core.update_sftp_sync_run_status(uuid, varchar, integer, bigint, integer, integer, integer, bigint, numeric, text);

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
            error_message = p_error_message,
            lease_expires_at = CASE
                WHEN p_status IN ('completed', 'failed', 'cancelled') THEN NULL
                WHEN p_lease_seconds IS NOT NULL THEN now() + make_interval(secs => p_lease_seconds)
                ELSE r.lease_expires_at
            END
        WHERE r.id = p_id
          AND r.status NOT IN ('completed', 'failed', 'cancelled')
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

    IF NOT FOUND THEN
        -- The run is already terminal (or missing): report the current state
        -- without mutating it so shutdown/sweep races cannot corrupt history.
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
        WHERE r.id = p_id;
    END IF;
END;
$$;

-- Lease-only heartbeat for active scans. The scanner calls this periodically
-- while it owns the queued/scanning run; a false return means recovery or a
-- terminal state won the race and the caller should stop heartbeating.
CREATE FUNCTION core.renew_sftp_sync_run_lease(
    p_id uuid,
    p_lease_seconds integer)
RETURNS boolean
LANGUAGE sql
AS $$
    WITH renewed AS (
        UPDATE core.sftp_sync_runs r
        SET lease_expires_at = now() + make_interval(secs => p_lease_seconds)
        WHERE r.id = p_id
          AND r.status IN ('queued', 'scanning')
        RETURNING 1
    )
    SELECT EXISTS(SELECT 1 FROM renewed);
$$;

-- ---------------------------------------------------------------------------
-- Leased claim. Replaces core.claim_next_download_queue_item():
-- * only claims items whose run is in 'downloading' (scan/enqueue finished);
-- * stamps a lease deadline so crashed workers can be detected.
-- ---------------------------------------------------------------------------
DROP FUNCTION IF EXISTS core.claim_next_download_queue_item();

CREATE FUNCTION core.claim_next_download_queue_item(p_lease_seconds integer)
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
          AND (qi.next_attempt_at IS NULL OR qi.next_attempt_at <= now())
          AND (qi.is_group = true OR (qi.is_group = false AND qi.group_remote_path IS NULL))
        ORDER BY qi.queued_at ASC, qi.id
        FOR UPDATE OF qi SKIP LOCKED
        LIMIT 1
    )
    UPDATE core.download_queue_items qi
    SET status = 'downloading',
        started_at = COALESCE(qi.started_at, now()),
        lease_expires_at = now() + make_interval(secs => p_lease_seconds),
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

-- ---------------------------------------------------------------------------
-- Lease renewal for long-running downloads. Returns false when the item is no
-- longer claimed (e.g. it was requeued by the recovery sweep).
-- ---------------------------------------------------------------------------
CREATE FUNCTION core.renew_download_queue_item_lease(
    p_id uuid,
    p_lease_seconds integer)
RETURNS boolean
LANGUAGE sql
AS $$
    WITH renewed AS (
        UPDATE core.download_queue_items qi
        SET lease_expires_at = now() + make_interval(secs => p_lease_seconds),
            updated_at = now()
        WHERE qi.id = p_id
          AND qi.status = 'downloading'
        RETURNING qi.id
    )
    SELECT count(*) > 0 FROM renewed;
$$;

-- ---------------------------------------------------------------------------
-- Graceful release of a claimed item (e.g. worker shutdown/cancellation):
-- returns the item to 'queued' without recording a failure. Returns zero rows
-- when the item is not currently claimed.
-- ---------------------------------------------------------------------------
CREATE FUNCTION core.release_download_queue_item(p_id uuid)
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
    UPDATE core.download_queue_items qi
    SET status = 'queued',
        bytes_downloaded = 0,
        current_bytes_per_second = NULL,
        error_message = NULL,
        handled_reason = NULL,
        started_at = NULL,
        completed_at = NULL,
        lease_expires_at = NULL,
        updated_at = now()
    WHERE qi.id = p_id
      AND qi.status = 'downloading'
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

-- ---------------------------------------------------------------------------
-- Recovery sweep, part 1: requeue claimed items whose lease expired (crashed
-- worker) — or all claimed items when p_ignore_lease is true (startup sweep in
-- the single-instance deployment). Increments retry_count for observability.
-- ---------------------------------------------------------------------------
CREATE FUNCTION core.requeue_stale_download_queue_items(p_ignore_lease boolean)
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
    UPDATE core.download_queue_items qi
    SET status = 'queued',
        bytes_downloaded = 0,
        current_bytes_per_second = NULL,
        error_message = NULL,
        handled_reason = NULL,
        started_at = NULL,
        completed_at = NULL,
        retry_count = qi.retry_count + 1,
        lease_expires_at = NULL,
        updated_at = now()
    WHERE qi.status = 'downloading'
      AND (p_ignore_lease OR qi.lease_expires_at IS NULL OR qi.lease_expires_at < now())
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

-- ---------------------------------------------------------------------------
-- Recovery sweep, part 2: reap orphaned runs.
-- * queued/scanning runs whose lease expired are marked failed (their scan
--   process died; queue items keep their state and are re-linked on next scan);
-- * downloading runs with no pending items are finalized as completed (worker
--   died between the last item and the run completion update).
-- Returns every run that was mutated.
-- ---------------------------------------------------------------------------
CREATE FUNCTION core.reap_orphaned_sftp_sync_runs(p_ignore_lease boolean)
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
    v_completed_ids uuid[];
    v_run_id uuid;
BEGIN
    RETURN QUERY
    WITH failed_runs AS (
        UPDATE core.sftp_sync_runs r
        SET status = 'failed',
            error_message = 'Run was interrupted before downloads started and was reaped by the recovery sweep.',
            completed_at = now(),
            lease_expires_at = NULL
        WHERE r.status IN ('queued', 'scanning')
          AND (p_ignore_lease OR r.lease_expires_at IS NULL OR r.lease_expires_at < now())
        RETURNING r.*
    )
    SELECT f.id,
           f.job_id,
           j.name AS job_name,
           f.status,
           f.started_at,
           f.completed_at,
           f.total_file_count,
           f.completed_file_count,
           f.skipped_file_count,
           f.failed_file_count,
           f.total_bytes,
           f.downloaded_bytes,
           f.current_bytes_per_second,
           f.error_message
    FROM failed_runs f
    INNER JOIN core.sftp_sync_jobs j ON j.id = f.job_id;

    SELECT array_agg(r.id)
    INTO v_completed_ids
    FROM core.sftp_sync_runs r
    WHERE r.status = 'downloading'
      AND NOT EXISTS (
          SELECT 1
          FROM core.download_queue_items qi
          WHERE qi.sync_run_id = r.id
            AND qi.status IN ('queued', 'comparing', 'downloading'));

    IF v_completed_ids IS NULL THEN
        RETURN;
    END IF;

    FOREACH v_run_id IN ARRAY v_completed_ids
    LOOP
        PERFORM core.recalculate_sftp_sync_run_aggregates(v_run_id);
    END LOOP;

    UPDATE core.sftp_sync_runs r
    SET status = 'completed',
        completed_at = now(),
        lease_expires_at = NULL
    WHERE r.id = ANY(v_completed_ids);

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
    WHERE r.id = ANY(v_completed_ids);
END;
$$;
