-- Reverse of 202605290001_AddQueueItemGrouping (Phase 1 down-migration).
-- Safe order: DROP INDEXes first (explicit, matching 202605270002_AddUiSupportIndexes_down.sql:1-7 pattern with
-- core. prefix + IF EXISTS), then DROP the three added columns (matching ALTER DROP COLUMN style from
-- 202605280002_UseGuidSystemPropertyIds_down.sql:5-18; comments are implicitly dropped with columns).
-- No other objects (functions, constraints) touched — per Phase 1 scope only.

DROP INDEX IF EXISTS core.ix_download_queue_items_sync_run_is_group;
DROP INDEX IF EXISTS core.ix_download_queue_items_job_group_remote;

ALTER TABLE core.download_queue_items
    DROP COLUMN child_count,
    DROP COLUMN group_remote_path,
    DROP COLUMN is_group;
