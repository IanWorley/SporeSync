DROP INDEX IF EXISTS core.ix_download_queue_items_next_attempt;

ALTER TABLE core.download_queue_items
    DROP COLUMN IF EXISTS next_attempt_at;
