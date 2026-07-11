CREATE FUNCTION core.safe_delete_sftp_sync_job(p_id uuid)
RETURNS text
LANGUAGE plpgsql
AS $$
BEGIN
    PERFORM 1 FROM core.sftp_sync_jobs WHERE id = p_id FOR UPDATE;
    IF NOT FOUND THEN
        RETURN 'not_found';
    END IF;

    IF EXISTS (
        SELECT 1 FROM core.sftp_sync_runs
        WHERE job_id = p_id AND status IN ('queued', 'scanning', 'downloading')
    ) THEN
        RETURN 'active_run';
    END IF;

    DELETE FROM core.download_queue_items WHERE job_id = p_id;
    DELETE FROM core.sftp_sync_runs WHERE job_id = p_id;
    DELETE FROM core.sftp_sync_jobs WHERE id = p_id;
    RETURN 'deleted';
END;
$$;

CREATE FUNCTION core.safe_delete_sftp_connection_profile(p_id uuid)
RETURNS text
LANGUAGE plpgsql
AS $$
BEGIN
    PERFORM 1 FROM core.sftp_connection_profiles WHERE id = p_id FOR UPDATE;
    IF NOT FOUND THEN
        RETURN 'not_found';
    END IF;

    IF EXISTS (SELECT 1 FROM core.sftp_sync_jobs WHERE connection_profile_id = p_id) THEN
        RETURN 'in_use';
    END IF;

    DELETE FROM core.sftp_connection_profiles WHERE id = p_id;
    RETURN 'deleted';
END;
$$;
