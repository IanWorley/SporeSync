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
