-- Retry budget, exponential backoff, and dead-lettering functions for download queue items.
--
-- Model:
-- - A transient download failure calls core.record_download_queue_item_failure, which increments
--   retry_count and either requeues the item with a backoff (next_attempt_at) or, once the retry
--   budget is exhausted, dead-letters it as a terminal 'failed' row (handled_reason =
--   'retry_budget_exhausted'). Dead-lettered items are never auto-requeued; they are revived only
--   when the remote file actually changes (upsert resets the budget) or via the manual retry
--   function below.
-- - core.defer_download_queue_item requeues an item without consuming retry budget (used for the
--   remote-file stability window, where the remote upload is still in progress).
-- - core.claim_next_download_queue_item respects next_attempt_at so backoff/deferral is honored.

-- Claim: skip items whose next attempt is scheduled in the future.
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
          AND (qi.next_attempt_at IS NULL OR qi.next_attempt_at <= now())
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

-- Upsert: a scan only re-upserts an item when it is new or the remote content changed, so the
-- retry budget and any scheduled backoff are reset for a fresh download of the new content.
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
        retry_count = 0,
        next_attempt_at = NULL,
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

-- Progress updates always clear any scheduled attempt (the item just ran, or reached a terminal state).
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
        next_attempt_at = NULL,
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

-- Records a failed download attempt. Atomically increments retry_count and either:
-- - requeues the item with next_attempt_at = p_next_attempt_at (backoff) while budget remains, or
-- - dead-letters it as terminal 'failed' with handled_reason = 'retry_budget_exhausted'.
-- p_max_retries is the number of retries allowed after the initial attempt.
-- p_bytes_downloaded optionally refreshes progress (e.g. group aggregates); NULL keeps the current value.
CREATE FUNCTION core.record_download_queue_item_failure(
    p_id uuid,
    p_error_message text,
    p_max_retries integer,
    p_next_attempt_at timestamptz,
    p_bytes_downloaded bigint DEFAULT NULL)
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
    SET retry_count = qi.retry_count + 1,
        status = CASE
            WHEN qi.retry_count + 1 > p_max_retries THEN 'failed'
            ELSE 'queued'
        END,
        next_attempt_at = CASE
            WHEN qi.retry_count + 1 > p_max_retries THEN NULL
            ELSE p_next_attempt_at
        END,
        handled_reason = CASE
            WHEN qi.retry_count + 1 > p_max_retries THEN 'retry_budget_exhausted'
            ELSE 'retry_scheduled'
        END,
        completed_at = CASE
            WHEN qi.retry_count + 1 > p_max_retries THEN now()
            ELSE NULL
        END,
        error_message = p_error_message,
        bytes_downloaded = COALESCE(p_bytes_downloaded, qi.bytes_downloaded),
        current_bytes_per_second = NULL,
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

-- Requeues an item without consuming retry budget (remote file inside the stability window).
CREATE FUNCTION core.defer_download_queue_item(
    p_id uuid,
    p_next_attempt_at timestamptz,
    p_reason varchar(100),
    p_bytes_downloaded bigint DEFAULT NULL)
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
    SET status = 'queued',
        next_attempt_at = p_next_attempt_at,
        handled_reason = p_reason,
        bytes_downloaded = COALESCE(p_bytes_downloaded, qi.bytes_downloaded),
        current_bytes_per_second = NULL,
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

-- Manual retry of a single dead-lettered ('failed') item. Resets the retry budget and requeues it.
-- For group rows, failed internal leaves are reset too so the group reprocesses its whole subtree.
-- Returns no row when the item does not exist or is not in a terminal 'failed' state.
CREATE FUNCTION core.retry_download_queue_item(p_id uuid)
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
DECLARE
    v_job_id uuid;
    v_remote_path varchar(2000);
    v_is_group boolean;
BEGIN
    SELECT qi.job_id, qi.remote_path, qi.is_group
    INTO v_job_id, v_remote_path, v_is_group
    FROM core.download_queue_items qi
    WHERE qi.id = p_id
      AND qi.status = 'failed';

    IF NOT FOUND THEN
        RETURN;
    END IF;

    IF v_is_group THEN
        UPDATE core.download_queue_items qi
        SET status = 'queued',
            retry_count = 0,
            next_attempt_at = NULL,
            error_message = NULL,
            handled_reason = NULL,
            current_bytes_per_second = NULL,
            completed_at = NULL,
            queued_at = now(),
            updated_at = now()
        WHERE qi.job_id = v_job_id
          AND qi.group_remote_path = v_remote_path
          AND qi.status = 'failed';
    END IF;

    RETURN QUERY
    UPDATE core.download_queue_items qi
    SET status = 'queued',
        retry_count = 0,
        next_attempt_at = NULL,
        error_message = NULL,
        handled_reason = NULL,
        current_bytes_per_second = NULL,
        completed_at = NULL,
        queued_at = now(),
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
END;
$$;

-- Bulk requeue of failed items is now a manual/administrative operation (the scan-time auto-requeue
-- call was removed to make dead-lettering terminal), so it also resets the retry budget.
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
        retry_count = 0,
        next_attempt_at = NULL,
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
