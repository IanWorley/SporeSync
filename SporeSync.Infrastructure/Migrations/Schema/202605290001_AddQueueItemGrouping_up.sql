-- Phase 1 — Schema & Migration (per folder-grouping-implementation-plan.html:304-317 and M1)
-- Adds grouping columns to core.download_queue_items for hybrid persistence model (locked decision #2).
-- See also: docs/grouping-rules.md (full authoritative algorithm, child_count semantics, trailing-/ convention, invariants),
--           docs/folder-grouping-implementation-plan.html (Phase 0 completion marker at ~302 with locked column spec,
--           Locked Design Decisions 216-239, Recommended Persistence Model 281-290, Execution Prompt),
--           SporeSync.Business/Scanning/ISftpDirectoryScanner.cs:29-34 and :124-128 (verbatim column + index spec + child_count=total-leaves).
--
-- Locked column spec (verbatim from committed Phase 0 artifacts; no deviations):
--   is_group boolean NOT NULL DEFAULT false,
--   group_remote_path varchar(2000) NULL,
--   child_count integer NOT NULL DEFAULT 0
-- child_count meaning (locked): total number of leaf files in the entire subtree for group rows (0 for loose files and for empty groups).
--   Secondary metadata only (bytes are primary per locked #3).
--
-- Indexes per locked spec (supporting (sync_run_id, is_group) and (job_id, group_remote_path) queries):
-- Original unique index ux_download_queue_items_job_remote_path remains valid (groups use dir-style remote_path ending with '/'; no collisions).
--
-- No data backfill logic needed beyond the DEFAULT clauses.
-- All existing rows in dev simulation (DevelopmentSimulationService.cs:120-142) and integration test seeds
-- (RepositoryIntegrationTests.cs:245-262, :285-302) are non-group flat files; the DEFAULT false/0/NULL makes them
-- correct hybrid non-group rows automatically. (Confirmed via exhaustive Grep/Read: all INSERTs use explicit column lists
-- that omit these columns; see also no-SELECT-* verification below.)
--
-- Postgres ALTER on potentially-populated table (in test containers or future dev DBs) is safe: constant DEFAULTs
-- populate existing rows efficiently (see analogous pattern with no explicit UPDATE in
-- 202605280002_UseGuidSystemPropertyIds_up.sql:5-6).
--
-- Down-migration cleanly reverses (indexes first, then columns).

ALTER TABLE core.download_queue_items
    ADD COLUMN is_group boolean NOT NULL DEFAULT false,
    ADD COLUMN group_remote_path varchar(2000) NULL,
    ADD COLUMN child_count integer NOT NULL DEFAULT 0;

CREATE INDEX ix_download_queue_items_sync_run_is_group
    ON core.download_queue_items (sync_run_id, is_group);

CREATE INDEX ix_download_queue_items_job_group_remote
    ON core.download_queue_items (job_id, group_remote_path);

COMMENT ON COLUMN core.download_queue_items.is_group IS 'Hybrid model flag: true for visible first-child opaque folder groups (locked decisions #1,#2; see grouping-rules.md:56-57); false for loose files and all internal leaf rows.';
COMMENT ON COLUMN core.download_queue_items.group_remote_path IS 'For leaf rows inside a group subtree: points back to the exact remote_path of their first-child group row (always ends with ''/'' per rules.md:108,155-158); NULL for visible groups and loose files.';
COMMENT ON COLUMN core.download_queue_items.child_count IS 'For group rows: total leaf file count in the entire subtree (0 for empty). For all other rows: 0. Secondary/audit only (bytes primary per locked #3 and rules.md:59,101,125,168).';
