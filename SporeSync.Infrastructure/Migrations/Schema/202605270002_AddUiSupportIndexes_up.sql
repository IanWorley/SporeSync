CREATE INDEX ix_sftp_sync_runs_status_started_at
ON core.sftp_sync_runs (status, started_at DESC);

CREATE INDEX ix_sftp_sync_runs_job_started_at
ON core.sftp_sync_runs (job_id, started_at DESC);

CREATE INDEX ix_download_queue_items_run_status
ON core.download_queue_items (sync_run_id, status);

CREATE INDEX ix_download_queue_items_run_queued_at
ON core.download_queue_items (sync_run_id, queued_at DESC);

CREATE INDEX ix_download_queue_items_run_completed_at
ON core.download_queue_items (sync_run_id, completed_at DESC);

CREATE INDEX ix_download_queue_items_run_remote_path
ON core.download_queue_items (sync_run_id, remote_path);

CREATE INDEX ix_download_queue_items_run_destination_path
ON core.download_queue_items (sync_run_id, destination_path);
