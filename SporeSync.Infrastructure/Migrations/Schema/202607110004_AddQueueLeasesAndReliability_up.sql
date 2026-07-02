-- Reliability follow-up 1 & 2: queue-item/run leases for crash recovery, plus an
-- active-run uniqueness guarantee so run creation can be made atomic.

ALTER TABLE core.download_queue_items
ADD COLUMN lease_expires_at timestamptz NULL;

COMMENT ON COLUMN core.download_queue_items.lease_expires_at IS
'Lease deadline for items claimed by a download worker (status = downloading). '
'Expired leases are requeued by the recovery sweep.';

ALTER TABLE core.sftp_sync_runs
ADD COLUMN lease_expires_at timestamptz NULL;

COMMENT ON COLUMN core.sftp_sync_runs.lease_expires_at IS
'Lease deadline for runs in pre-download statuses (queued/scanning). '
'Expired leases mark the run as failed via the recovery sweep.';

-- Supports the recovery sweep scanning for stale claimed items.
CREATE INDEX ix_download_queue_items_downloading_lease
ON core.download_queue_items (lease_expires_at)
WHERE status = 'downloading';

-- Close out duplicate active runs (keep the newest per job) so the partial unique
-- index below can be created. Historically the scheduler could race and create
-- more than one active run per job.
WITH ranked AS (
    SELECT r.id,
           row_number() OVER (PARTITION BY r.job_id ORDER BY r.started_at DESC, r.id DESC) AS rn
    FROM core.sftp_sync_runs r
    WHERE r.status IN ('queued', 'scanning', 'downloading')
)
UPDATE core.sftp_sync_runs r
SET status = 'failed',
    error_message = 'Run was superseded by a newer active run and closed during the reliability migration.',
    completed_at = now()
FROM ranked
WHERE r.id = ranked.id
  AND ranked.rn > 1;

-- At most one active run per job; enables atomic run creation via ON CONFLICT.
CREATE UNIQUE INDEX ux_sftp_sync_runs_active_job
ON core.sftp_sync_runs (job_id)
WHERE status IN ('queued', 'scanning', 'downloading');
