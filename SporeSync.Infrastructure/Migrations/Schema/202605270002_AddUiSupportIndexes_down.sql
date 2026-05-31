DROP INDEX IF EXISTS core.ix_download_queue_items_run_destination_path;
DROP INDEX IF EXISTS core.ix_download_queue_items_run_remote_path;
DROP INDEX IF EXISTS core.ix_download_queue_items_run_completed_at;
DROP INDEX IF EXISTS core.ix_download_queue_items_run_queued_at;
DROP INDEX IF EXISTS core.ix_download_queue_items_run_status;
DROP INDEX IF EXISTS core.ix_sftp_sync_runs_job_started_at;
DROP INDEX IF EXISTS core.ix_sftp_sync_runs_status_started_at;
