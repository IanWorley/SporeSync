-- Retention pruning for unbounded history tables (runs + queue items).
--
-- Queue items double as per-file sync state (unique on job_id + remote_path),
-- so they are detached from pruned runs instead of deleted; completed state is
-- preserved to avoid re-downloading unchanged files. Only 'remote_deleted'
-- markers are physically removed, because the upsert path recreates them if a
-- remote file with the same path ever reappears.
CREATE FUNCTION core.prune_sftp_sync_history(p_cutoff timestamptz)
RETURNS TABLE (
    pruned_run_count integer,
    pruned_queue_item_count integer)
LANGUAGE plpgsql
AS $$
DECLARE
    v_run_ids uuid[];
    v_pruned_runs integer := 0;
    v_pruned_items integer := 0;
BEGIN
    SELECT array_agg(r.id)
    INTO v_run_ids
    FROM core.sftp_sync_runs r
    WHERE r.status IN ('completed', 'failed', 'cancelled')
      AND COALESCE(r.completed_at, r.started_at) < p_cutoff;

    DELETE FROM core.download_queue_items qi
    WHERE qi.status = 'skipped'
      AND qi.handled_reason = 'remote_deleted'
      AND qi.updated_at < p_cutoff
      AND (qi.sync_run_id IS NULL
           OR (v_run_ids IS NOT NULL AND qi.sync_run_id = ANY(v_run_ids)));

    GET DIAGNOSTICS v_pruned_items = ROW_COUNT;

    IF v_run_ids IS NOT NULL THEN
        UPDATE core.download_queue_items qi
        SET sync_run_id = NULL
        WHERE qi.sync_run_id = ANY(v_run_ids);

        DELETE FROM core.sftp_sync_runs r
        WHERE r.id = ANY(v_run_ids);

        GET DIAGNOSTICS v_pruned_runs = ROW_COUNT;
    END IF;

    RETURN QUERY SELECT v_pruned_runs, v_pruned_items;
END;
$$;
