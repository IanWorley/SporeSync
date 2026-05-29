-- Phase 3 — Backend Models, DTOs, Services (per folder-grouping-implementation-plan.html:337-344 and M1).
-- Extends the SQL projections (the two UI queue functions + the internal group leaves helper)
-- to return the three new grouping columns added in Phase 1.
--
-- This is required so that the C# readers (repo ReadItem, sim ReadQueueItem, controller mapping)
-- can hydrate the new properties on DownloadQueueItem / DownloadQueueItemResponse.
--
-- References:
-- - Phase 3 plan bullets (html:339-343)
-- - Phase 2 completion marker + visible filter + internal helper (html:335 + 202605290002_..._up.sql)
-- - Phase 1 locked columns + child_count=total-leaves (html:319 + 202605290001_..._up.sql:30-33)
-- - grouping-rules.md invariants (esp. #4 no leaf leakage in paged queries, #6 visible first-child counts)
-- - Subagent Phase 3 proposal (ID 8b5dd778...) after mandatory reads of plan + all call sites.
--
-- All changes are additive. Existing flat rows (from sim + tests) will return false/NULL/0 for the new columns.
-- The visible filter (Phase 2) remains unchanged.
-- Down-migration restores the exact Phase 2 function bodies (without the 3 columns).

-- Extend the main paged UI function (get_download_queue_items)
-- Must DROP first because Postgres does not allow CREATE OR REPLACE to change the return type of an existing function.
DROP FUNCTION IF EXISTS core.get_download_queue_items(uuid, text[], text, text, text, integer, integer);
CREATE FUNCTION core.get_download_queue_items(
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
    updated_at timestamptz,
    -- Phase 3 additions (plan:339)
    is_group boolean,
    group_remote_path varchar(2000),
    child_count integer)
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
               qi.updated_at,
               qi.is_group,
               qi.group_remote_path,
               qi.child_count
        FROM core.download_queue_items qi
        WHERE qi.sync_run_id = p_run_id
          AND (p_statuses IS NULL OR qi.status = ANY(p_statuses))
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
           fi.updated_at,
           fi.is_group,
           fi.group_remote_path,
           fi.child_count
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

-- count_ function does not return rows, so no column change needed (filter already updated in Phase 2).

-- Extend the internal worker helper (get_download_queue_group_leaves)
DROP FUNCTION IF EXISTS core.get_download_queue_group_leaves(uuid, varchar(2000));
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
    updated_at timestamptz,
    -- Phase 3 additions
    is_group boolean,
    group_remote_path varchar(2000),
    child_count integer)
LANGUAGE sql
AS $$
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
           qi.updated_at,
           qi.is_group,
           qi.group_remote_path,
           qi.child_count
    FROM core.download_queue_items qi
    WHERE qi.sync_run_id = p_run_id
      AND qi.group_remote_path = p_group_remote_path
      AND qi.is_group = false;
$$;
