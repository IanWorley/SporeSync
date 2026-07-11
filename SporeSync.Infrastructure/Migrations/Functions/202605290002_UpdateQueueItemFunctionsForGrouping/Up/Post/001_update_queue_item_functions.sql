-- Phase 2 — SQL Functions & Aggregates (per folder-grouping-implementation-plan.html:321-333 and M1).
-- Updates the two UI-facing queue item functions to enforce the hybrid visible-only filter for first-child opaque grouping.
-- Adds the small internal helper requested for worker requeue/resume of group subtrees.
--
-- References (do not deviate):
-- - Locked Design Decisions #1 (first-child granularity only), #2 (hybrid persistence), #3 (bytes primary) in plan.html:216-239.
-- - Recommended Persistence Model — Hybrid (plan.html:281-290).
-- - Phase 2 exact requirements (plan.html:323-332).
-- - Phase 1 completion marker + locked column spec (plan.html:319).
-- - Authoritative grouping algorithm: specs/grouping-rules.md (visible rows definition at 55-59 + 137, Search Behavior 136-139, Invariants esp. #4 at 170 "Default paged queue queries never return any row where group_remote_path IS NOT NULL", How group_remote_path Links 155-158, Requeue 129-134, Byte-Size 123-127).
-- - Phase 1 columns (202605290001_AddQueueItemGrouping_up.sql:30-33 + indexes + comments).
-- - Subagent proposal (ID 33cce51a...) after 30+ file:line Reads/Globs/Greps of functions, call sites, seeds, etc.
--
-- Changes are purely additive:
-- - Signatures of get_download_queue_items and count_download_queue_items unchanged (17 cols returned, same parameters).
-- - Visible filter added: AND (qi.is_group = true OR (qi.is_group = false AND qi.group_remote_path IS NULL)).
--   This is a no-op on all current data (flat non-group rows from sim + tests get DEFAULT false/0/NULL from Phase 1).
-- - For group rows: the stored file_size_bytes / bytes_downloaded etc. (subtree aggregates maintained by scanner/worker) are returned directly.
-- - Search (ILIKE) and all existing sort keys (basename, size, progress, status, queuedAt, etc.) continue to work on visible groups.
-- - New internal helper core.get_download_queue_group_leaves for future worker (requeue/resume per plan:332 + rules requeue section).
--
-- Run aggregate recalc helpers (sim + any in functions) are explicitly deferred in this phase (see subagent rationale: current flat-only world makes them equivalent to visible; real update aligns with Phase 6 sim seed changes when groups+leaves first appear).
--
-- Down-migration cleanly drops only the new internal helper (main function updates are via OR REPLACE; full rollback of the 202605280001 functions migration reverts the bodies).

CREATE OR REPLACE FUNCTION core.get_download_queue_items(
    p_run_id uuid,
    p_statuses text[],
    p_search text,
    p_sort_by text,
    p_sort_direction text,
    p_page_size integer,
    p_offset integer)
RETURNS TABLE (
    id uuid,
    job_id uuid,
    sync_run_id uuid,
    remote_path varchar(2000),
    destination_path varchar(2000),
    file_size_bytes bigint,
    remote_modified_at timestamptz,
    status varchar(32),
    bytes_downloaded bigint,
    current_bytes_per_second numeric(20, 2),
    retry_count integer,
    handled_reason varchar(100),
    error_message text,
    queued_at timestamptz,
    started_at timestamptz,
    completed_at timestamptz,
    updated_at timestamptz)
LANGUAGE sql
AS $$
    WITH filtered_items AS (
        SELECT qi.id,
               qi.job_id,
               qi.sync_run_id,
               qi.remote_path,
               qi.destination_path,
               qi.file_size_bytes,
               qi.remote_modified_at,
               qi.status,
               qi.bytes_downloaded,
               qi.current_bytes_per_second,
               qi.retry_count,
               qi.handled_reason,
               qi.error_message,
               qi.queued_at,
               qi.started_at,
               qi.completed_at,
               qi.updated_at
        FROM core.download_queue_items qi
        WHERE qi.sync_run_id = p_run_id
          AND (p_statuses IS NULL OR qi.status = ANY(p_statuses))
          -- Visible filter (Phase 2 / hybrid model / locked #1 + #2 + rules.md:137 + invariant #4):
          -- Only groups (is_group=true) or top-level loose files (is_group=false AND group_remote_path IS NULL).
          -- Internal leaves (group_remote_path NOT NULL) are never returned to UI/paged API/dashboard.
          -- During transition (post-Phase 1, pre-Phase 6 seeds), this is a no-op because all rows are flat
          -- (is_group=false from DEFAULT + explicit INSERTs omitting the col; group_remote_path=NULL).
          AND (qi.is_group = true OR (qi.is_group = false AND qi.group_remote_path IS NULL))
          AND (p_search IS NULL OR qi.remote_path ILIKE p_search OR qi.destination_path ILIKE p_search)
    )
    SELECT fi.id,
           fi.job_id,
           fi.sync_run_id,
           fi.remote_path,
           fi.destination_path,
           fi.file_size_bytes,
           fi.remote_modified_at,
           fi.status,
           fi.bytes_downloaded,
           fi.current_bytes_per_second,
           fi.retry_count,
           fi.handled_reason,
           fi.error_message,
           fi.queued_at,
           fi.started_at,
           fi.completed_at,
           fi.updated_at
    FROM filtered_items fi
    ORDER BY
        CASE WHEN lower(p_sort_direction) = 'asc' THEN
            CASE p_sort_by
                WHEN 'status' THEN fi.status::text
                WHEN 'basename' THEN regexp_replace(fi.remote_path, '^.*/', '')
                WHEN 'path' THEN fi.remote_path::text
            END
        END ASC,
        CASE WHEN lower(p_sort_direction) <> 'asc' THEN
            CASE p_sort_by
                WHEN 'status' THEN fi.status::text
                WHEN 'basename' THEN regexp_replace(fi.remote_path, '^.*/', '')
                WHEN 'path' THEN fi.remote_path::text
            END
        END DESC,
        CASE WHEN lower(p_sort_direction) = 'asc' THEN
            CASE p_sort_by
                WHEN 'size' THEN fi.file_size_bytes::numeric
                WHEN 'progress' THEN CASE WHEN fi.file_size_bytes = 0 THEN 0 ELSE fi.bytes_downloaded::numeric / fi.file_size_bytes END
            END
        END ASC,
        CASE WHEN lower(p_sort_direction) <> 'asc' THEN
            CASE p_sort_by
                WHEN 'size' THEN fi.file_size_bytes::numeric
                WHEN 'progress' THEN CASE WHEN fi.file_size_bytes = 0 THEN 0 ELSE fi.bytes_downloaded::numeric / fi.file_size_bytes END
            END
        END DESC,
        CASE WHEN lower(p_sort_direction) = 'asc' THEN
            CASE p_sort_by
                WHEN 'completedAt' THEN fi.completed_at
                ELSE fi.queued_at
            END
        END ASC,
        CASE WHEN lower(p_sort_direction) <> 'asc' THEN
            CASE p_sort_by
                WHEN 'completedAt' THEN fi.completed_at
                ELSE fi.queued_at
            END
        END DESC,
        fi.queued_at DESC,
        fi.id
    LIMIT p_page_size OFFSET p_offset;
$$;

CREATE OR REPLACE FUNCTION core.count_download_queue_items(
    p_run_id uuid,
    p_statuses text[],
    p_search text)
RETURNS bigint
LANGUAGE sql
AS $$
    SELECT count(*)
    FROM core.download_queue_items qi
    WHERE qi.sync_run_id = p_run_id
      AND (p_statuses IS NULL OR qi.status = ANY(p_statuses))
      -- Visible filter (exact match to get_download_queue_items; see comments there for full rationale,
      -- citations to rules.md:137/170, plan:324, locked #1/#2, invariant #4).
      AND (qi.is_group = true OR (qi.is_group = false AND qi.group_remote_path IS NULL))
      AND (p_search IS NULL OR qi.remote_path ILIKE p_search OR qi.destination_path ILIKE p_search);
$$;

-- New internal helper for the worker (Phase 2 per plan:332 exactly).
-- Fetches the leaf rows under a specific first-child group for requeue/resume/partial progress.
-- Ties directly to rules.md requeue (129-134), linking (155-158), and hybrid invariants.
-- Never used by UI paths (enforces no leaf leakage per invariant #4).
CREATE FUNCTION core.get_download_queue_group_leaves(
    p_run_id uuid,
    p_group_remote_path varchar(2000))
RETURNS TABLE (
    id uuid,
    job_id uuid,
    sync_run_id uuid,
    remote_path varchar(2000),
    destination_path varchar(2000),
    file_size_bytes bigint,
    remote_modified_at timestamptz,
    status varchar(32),
    bytes_downloaded bigint,
    current_bytes_per_second numeric(20, 2),
    retry_count integer,
    handled_reason varchar(100),
    error_message text,
    queued_at timestamptz,
    started_at timestamptz,
    completed_at timestamptz,
    updated_at timestamptz)
LANGUAGE sql
AS $$
    -- Internal/debug helper only (Phase 2 per plan:332; for future worker requeue/resume/partial progress on subtree).
    -- Fetches exactly the leaf rows (is_group=false) linked to a specific first-child group via group_remote_path.
    -- Ties directly to requeue rules (rules.md:130-132: reset group + "WHERE group_remote_path = ..."), byte invariants (123-127), and hybrid persistence (locked #2).
    -- The (job_id, group_remote_path) index (Phase 1) supports efficient lookup.
    -- Never called from UI paths (get_download_queue_items + count_ enforce visible-only; see invariant #4 at rules.md:170).
    -- Worker (later phases) can use this + the group row for full subtree context without re-scanning.
    SELECT qi.id,
           qi.job_id,
           qi.sync_run_id,
           qi.remote_path,
           qi.destination_path,
           qi.file_size_bytes,
           qi.remote_modified_at,
           qi.status,
           qi.bytes_downloaded,
           qi.current_bytes_per_second,
           qi.retry_count,
           qi.handled_reason,
           qi.error_message,
           qi.queued_at,
           qi.started_at,
           qi.completed_at,
           qi.updated_at
    FROM core.download_queue_items qi
    WHERE qi.sync_run_id = p_run_id
      AND qi.group_remote_path = p_group_remote_path
      AND qi.is_group = false;
$$;
