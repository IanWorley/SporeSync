-- Retry budget / exponential backoff / dead-lettering support for download queue items.
-- next_attempt_at is the earliest time a 'queued' item may be claimed again. It is set by the
-- worker when a transient failure schedules a backoff retry, or when a remote file is still
-- inside the stability window. NULL means the item is immediately claimable.
ALTER TABLE core.download_queue_items
    ADD COLUMN next_attempt_at timestamptz NULL;

COMMENT ON COLUMN core.download_queue_items.next_attempt_at IS
    'Earliest time the item may be claimed again (retry backoff or remote-stability deferral). NULL = immediately claimable.';

CREATE INDEX ix_download_queue_items_next_attempt
ON core.download_queue_items (next_attempt_at)
WHERE status = 'queued';
