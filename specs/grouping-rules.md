# SporeSync First-Child Opaque Folder Grouping Rules

Backend-driven implementation plan for first-child opaque folder grouping, recursive nested downloads, size-based progress, and requeue support.

**Primary tracking:** Byte sizes (folder + file) rather than raw leaf-file counts.

## Locked Design Decisions (from planning discussion)

**1. First-child granularity (A)**
Only the immediate children under the job's configured `sourcePath` appear as visible rows. Deeper nesting is never shown in the normal paged queue view.

**2. Persistence model — Hybrid (recommended & accepted)**
Store both the visible top-level opaque groups **and** the individual leaf files internally. UI-facing queries (and the dashboard) only ever surface the groups with aggregated stats. This enables reliable requeue, partial progress, per-file error context, and future "expand details" capability without a painful migration.

**3. Progress & counting**
Primary success/progress metric is bytes transferred (sum of file sizes inside groups + loose files). Integer "file counts" on runs become secondary / logical (number of visible groups + loose files at enqueue time).

**4. Requeue behavior**
The system must support requeuing a failed opaque folder group (and its subtree) for another full attempt. The engine can retry the group as a unit.

**5. Opaque + recursive download**
Folders are opaque in the UI. When the worker processes a folder group it must recursively walk and transfer the entire subtree, preserving relative structure under the job's `destinationPath`.

## Concrete Example

**Job:** `sourcePath = "/remote/incoming/"`, `destinationPath = "/data/incoming/"`

**Remote tree at scan time:**
```
/remote/incoming/
├── reports/
│   ├── 2026/
│   │   └── Q1-sales.pdf
│   └── summary.xlsx
├── customers.csv
└── archive/
    └── old/
        └── backup.zip
```

**Visible opaque rows in the run's queue (paged API + dashboard table):**
- `reports/` — opaque group (represents 3 files, all bytes under it)
- `customers.csv` — loose file
- `archive/` — opaque group (represents 1 file deep inside)

Nothing under `reports/` or `archive/` ever appears as its own row in the normal queue list unless a future "expand" feature is added.

(This block is verbatim from folder-grouping-implementation-plan.html:244-263.)

## The First-Child Opaque Grouping Algorithm

### Definitions
- **Job's sourcePath**: configured remote starting path (from `sftp_sync_jobs.source_path`, varchar(1000)). May or may not end with `/`. The scanner determines at runtime whether it targets a file or directory.
- **First-child entries**: the immediate direct children (files or immediate subdirectories) directly under the (normalized) sourcePath. *Only these* become visible queue rows. Deeper descendants are never materialized as top-level rows.
- **Visible queue rows** (what the paged API + dashboard see): exactly the first-child entries for the job's sourcePath at enqueue/scan time for that run. Each is either:
  - **Loose file row**: `is_group = false`, `group_remote_path = NULL`.
  - **Opaque group row**: `is_group = true`, `remote_path` *always ends with `/`*, represents its entire subtree.
- **Leaf rows** (internal/persisted for hybrid model only): `is_group = false`, `group_remote_path` set to the first-child group's `remote_path` value. Never returned by default paged queue queries (unless a future debug `includeLeaves` flag).
- **Group row aggregates** (hybrid): a group row re-uses the existing `file_size_bytes`, `bytes_downloaded`, etc. columns to hold *subtree totals* (sum of its leaves). `child_count` on the group = total descendant leaves.

### Normalization Rules for sourcePath (and file vs. directory target detection)
1. The stored `sourcePath` value is used exactly as the prefix.
2. The future scanner (see Phase 4 contract) MUST perform an SFTP STAT (or equivalent) on the exact `sourcePath` to decide:
   - **File target**: sourcePath resolves to a regular file (or is treated as one). Enqueue exactly one visible loose file row using the `sourcePath` value verbatim as `remote_path`. `is_group=false`, `group_remote_path=NULL`. No recursion, no other rows.
   - **Directory target** (including non-existent paths treated as dirs for forward compatibility): proceed to first-child listing. Normalize for child construction:
     - Let `s = sourcePath.Trim()`.
     - If `s` does not end with `/`, append `/` → this is `normalizedSource`.
     - Examples:
       - `"/remote/incoming"` → `"/remote/incoming/"`
       - `"/remote/incoming/"` → `"/remote/incoming/"`
       - `"/remote/incoming/reports"` (dir target) → `"/remote/incoming/reports/"`
3. All constructed `remote_path` values for children (and their descendant leaves) are built from this `normalizedSource`. This guarantees:
   - Group `remote_path` values *always end with `/`* (e.g. `"/remote/incoming/reports/"`).
   - File `remote_path` values never end with `/`.
   - The `(job_id, remote_path)` unique constraint remains valid (different strings even for basename collisions such as a file "data" vs. sibling dir "data/").
4. No existing code in the codebase performs trailing-slash normalization on `SourcePath` (confirmed via exhaustive Grep across all .cs/.ts/.tsx files). The first implementation of the scanner contract owns this rule.

### Enqueue / Scan-Time Algorithm
When a run begins scanning/enqueuing for a job (future worker or sim update):

- Perform STAT on job `sourcePath` (file vs. dir decision as above).

- **File target case** (see definition above): create exactly one row (the visible loose file). Set `child_count=0`, `group_remote_path=NULL`, `is_group=false`. Use STAT size for `file_size_bytes`. Done.

- **Directory target case**:
  - List *only immediate* children (non-recursive LIST/READDIR).
  - For each immediate child (name + isDir flag + size/mtime if available from the listing):
    - `childRemote = normalizedSource + childName + (isDir ? "/" : "")`
    - Compute analogous `childDest` from job `destinationPath` (normalize it the same way, then append).
    - **If child is a directory (opaque group)**:
      - Recursively walk the *entire* subtree under this child to discover *every* leaf file (full remote paths, individual sizes, mtimes).
      - Compute:
        - `totalBytes = SUM` of all discovered leaf file sizes (0 if empty subtree).
        - `totalLeaves = COUNT` of all discovered leaf files (0 if empty).
      - Create the **group row** (visible):
        - `remote_path = childRemote` (ends with `/`)
        - `destination_path = childDest` (the subtree root dir)
        - `is_group = true`
        - `group_remote_path = NULL` (groups are roots)
        - `file_size_bytes = totalBytes`
        - `child_count = totalLeaves`
        - `remote_modified_at = MAX` mtime across the subtree leaves (or NULL)
        - All other fields: `status='queued'`, `bytes_downloaded=0`, etc.
      - For *every* discovered leaf file in this subtree, create a **leaf row** (internal):
        - `remote_path = full leaf remote path`
        - `destination_path = fully computed leaf destination (job dest + relative path)`
        - `is_group = false`
        - `group_remote_path = childRemote`  ← **the link back to the first-child group**
        - `file_size_bytes =` the leaf's individual size
        - `child_count = 0`
        - Its own `remote_modified_at`, etc.
    - **Else (immediate child is a loose file)**:
      - Create a single visible **loose file row**:
        - `remote_path = childRemote` (no trailing `/`)
        - `is_group = false`
        - `group_remote_path = NULL`
        - `file_size_bytes =` size from listing
        - `child_count = 0`
  - **No rows** are ever created for intermediate subdirectories (e.g. no row for `reports/2026/`). Only first-child groups + all their descendant leaves + top-level loose files.

- **Empty immediate child directories**: Still create the group row with `child_count=0`, `file_size_bytes=0`. This preserves exact first-child structure from the remote at scan time. (Rationale tied to locked #1 and #4: requeue support must be able to target the structure even if content appears later.)

### Byte-Size Capture at Scan Time
- All sizes originate from SFTP server metadata at enqueue/scan time (STAT or LIST sizes for files; recursive summation for groups).
- **Invariant**: For any group row, `file_size_bytes` on the group == `SUM(file_size_bytes)` over all its linked leaf rows (where `group_remote_path` matches the group's `remote_path`). The same holds for `child_count`.
- During later download the worker keeps group aggregates in sync (by updating the group row's `bytes_downloaded` etc. as leaves progress). Leaves are also updated individually (for per-file error context and requeue).
- Bytes are *always* the primary metric. `child_count` and run-level `total_file_count` are secondary/logical.

### Requeue Behavior for a Group (and its subtree)
- Requeue of a failed group row resets the group row (`status='queued'`, `bytes_downloaded=0`, `error_message=NULL`, etc.).
- It *also* resets every descendant leaf row (`WHERE group_remote_path =` the group's remote_path) the same way.
- The worker can then pick up the group row again and re-execute the full recursive subtree transfer (or use persisted leaf state for resume, per the hybrid model).
- Requeue of a loose file affects only that row.
- This satisfies locked decision #4 exactly. The persisted leaves (hybrid) make reliable subtree requeue possible without requiring a fresh SFTP walk on every retry.

**Phase 5 (2026-05-29) — Requeue, Progress & Broadcasting Semantics (M4)**: Per plan:360-366, the precise contracts have been defined (no code changes in this phase):
- Requeue of a failed opaque group is a subtree unit op (reset the visible group row + all its internal leaves via the existing `GetLeavesForGroupAsync` + `WHERE group_remote_path = ...` pattern; scanner not re-invoked).
- Progress contract: hybrid (maintain aggregate on the group row's `bytes_downloaded` etc. as leaves advance; recommended for consistency with scan-time aggregates, Phase 2/3 visible filters, and locked #2/#3).
- Successful completion: mark the visible group row complete (optionally mark leaves for audit in hybrid model); normal visible run recalc + broadcast rolls up automatically.
- Broadcaster readiness: `DashboardBroadcaster.QueueItemUpdatedAsync` + run-group subscription already suffice for groups (see the Phase 5 comments added to `DashboardBroadcaster.cs:15` and `IDashboardBroadcaster.cs:9`). Clients remain fully opaque; broadcasting the updated visible group row is the only signal needed.
These semantics are the authoritative reference for Phases 6+ (simulation, worker, requeue endpoint). All existing flat simulation paths, SQL visible filters, and public APIs are unaffected.

### Search Behavior on Groups
- Normal paged queue queries (after Phase 2) return only visible rows: `is_group = true` OR (`is_group = false` AND `group_remote_path IS NULL`).
- `ILIKE` search on `remote_path` / `destination_path` therefore works directly against group dir paths (e.g. searching "reports" or "archive" matches the opaque group rows).
- Deep leaf paths are invisible to normal search (unless debug includeLeaves is used later).

### Edge Cases & Notes
- A job whose `sourcePath` points at a specific deep file (e.g. `/remote/incoming/reports/2026/Q1-sales.pdf`) is a file target: exactly one loose file row. No automatic grouping.
- Basename collisions at the first-child level (file "foo" + sibling dir "foo/"): their `remote_path` values differ by the trailing `/`, so the unique constraint is satisfied and UI `basename()` will naturally show the distinction.
- All remote paths are assumed absolute and start with `/` (consistent with current simulation data and the concrete example).
- Very deep / huge / symlink / special-file trees: deferred (see plan "Deferred / Out of Scope").
- At enqueue time the run's `total_file_count` (and completed/failed/skipped counters) = *number of visible first-child entries created* (groups + loose files). It is **not** `SUM(child_count)` over groups. Physical leaf totals are available via `SUM(child_count)` + count of loose files when needed for auditing.
- The hybrid model guarantees that every leaf under a first-child group has a persisted row linked by `group_remote_path`. This is what enables requeue (#4), partial-failure visibility, and future expand without schema pain.

### Why the trailing-/ convention for groups
- Makes group paths visually directory-like in logs, errors, and debug.
- Unambiguously distinguishes groups from files even without consulting `is_group`.
- Eliminates any possibility of `(job_id, remote_path)` collisions between a file and a same-named sibling directory.
- Directly matches the Concrete Example (`reports/`, `archive/`).

## How group_remote_path Links Leaves
- Only descendant leaves of a first-child group have `group_remote_path` set (to the exact string value of the group's `remote_path`, including its trailing `/`).
- Top-level loose files have `group_remote_path = NULL`.
- Efficient subtree lookup for a group (worker/requeue/debug): `WHERE group_remote_path = @groupRemote AND is_group = false`.
- The `(job_id, group_remote_path)` index (proposed in column spec) supports this.

## Relation to Job sourcePath / destinationPath
- Every first-child group's `remote_path` starts with the (normalized) job `sourcePath`.
- Group and leaf `destination_path` values must preserve relative structure under the job `destinationPath`.
- Example: job source `/remote/incoming/` + dest `/data/incoming/` → group `reports/` gets `destination_path = "/data/incoming/reports/"`; its leaves get full per-file dest paths under that.

## Invariants (must be maintained by scanner + worker + SQL updates)
1. Group `file_size_bytes` == SUM of its leaves' `file_size_bytes`.
2. Group `child_count` == COUNT of its leaves.
3. No leaf row's `remote_path` would qualify it as a first-child under the same job/source.
4. Default paged queue queries never return any row where `group_remote_path IS NOT NULL`.
5. `(job_id, remote_path)` unique is never violated.
6. Run-level file counts reflect visible first-child cardinality (not physical leaf counts).

## Version & Provenance
- Created during Phase 0 (Preparation & Contracts) analysis on 2026-05-28.
- Verbatim Concrete Example + Locked Decisions copied from folder-grouping-implementation-plan.html.
- All rules derived strictly from the 5 locked decisions + the "first-child only" + "hybrid" + "bytes primary" + "opaque" + "full requeue" requirements.
- This document (plus the column spec) is the single source of truth for the grouping algorithm. Future scanner contract, worker, SQL functions, and tests must follow it exactly; any deviation requires explicit user approval + plan update.

(This is the complete proposed content for the new `specs/grouping-rules.md`.)
