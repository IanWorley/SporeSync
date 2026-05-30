-- Reverse of 202605290002_UpdateQueueItemFunctionsForGrouping (Phase 2 down-migration).
-- Drops only the new internal helper (the two main functions were updated via CREATE OR REPLACE;
-- rolling back the original 202605280001 functions migration will restore the prior bodies).
-- Safe, minimal, and consistent with how function-only changes are handled in this project.

DROP FUNCTION IF EXISTS core.get_download_queue_group_leaves(uuid, varchar(2000));
