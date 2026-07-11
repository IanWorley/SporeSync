CREATE OR REPLACE FUNCTION core.requeue_failed_download_queue_items(
    p_job_id uuid,
    p_sync_run_id uuid)
RETURNS integer
LANGUAGE plpgsql
AS $$
DECLARE
    v_count integer;
BEGIN
    WITH failed_items AS (
        SELECT qi.id,
               qi.remote_path,
               qi.group_remote_path,
               qi.is_group
        FROM core.download_queue_items qi
        WHERE qi.job_id = p_job_id
          AND qi.status = 'failed'
    ),
    owning_groups AS (
        SELECT group_qi.id
        FROM core.download_queue_items group_qi
        INNER JOIN failed_items failed_leaf
            ON failed_leaf.is_group = false
           AND failed_leaf.group_remote_path IS NOT NULL
           AND group_qi.job_id = p_job_id
           AND group_qi.is_group = true
           AND group_qi.remote_path = failed_leaf.group_remote_path
    ),
    rows_to_requeue AS (
        SELECT id FROM failed_items
        UNION
        SELECT id FROM owning_groups
    )
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
    WHERE qi.id IN (SELECT id FROM rows_to_requeue);

    GET DIAGNOSTICS v_count = ROW_COUNT;
    RETURN v_count;
END;
$$;
