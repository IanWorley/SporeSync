DROP INDEX IF EXISTS core.ux_sftp_sync_runs_active_job;
DROP INDEX IF EXISTS core.ix_download_queue_items_downloading_lease;

ALTER TABLE core.sftp_sync_runs
DROP COLUMN IF EXISTS lease_expires_at;

ALTER TABLE core.download_queue_items
DROP COLUMN IF EXISTS lease_expires_at;
