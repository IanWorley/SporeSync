# SporeSync starting brief

SporeSync downloads files from a seedbox onto the machine running its backend.
The web dashboard shows discovered files and download progress. Closing the
browser should not interrupt a transfer. This brief records the direction for
the new implementation; it is not a migration plan for the earlier codebase.

## Agreed behavior

- Discover files using a small Python scanner uploaded to the seedbox and
  periodically executed over SSH. Schedule scans from the SporeSync host.
- Configure the remote source directory and the host's local download directory.
- Download files over SFTP and show their progress in the frontend.
- Enable automatic downloading by default. Make scanning configurable, including
  the source directory and scan interval.
- Copy one way from the seedbox to the host, retaining source files unless the user
  explicitly deletes them. Do not propagate local deletions or upload local changes.
- Support per-file pause and resume. Retain partial data and paused state across
  restarts. Confirm local or server deletion in the dashboard. Local deletion
  requeues the file; server deletion retains local data and stops that source's jobs.
- With automatic downloads enabled, stable scans requeue completed files deleted
  outside SporeSync when the matching remote file is still present.
- When a remote file is larger than the existing local file, resume downloading
  from the local byte count and append the remaining bytes. In temporary-file
  mode, apply the same behavior to the partial download. Size alone does not
  establish that an existing local file is a matching prefix of the remote file;
  existing prefixes are verified byte for byte and replacement conflicts fail safely.
- Make temporary `.part` files optional: when enabled, download to the temporary
  name and rename after completion; when disabled, write to the final filename.
  Track completion independently of the filename in both modes.

## Proposed starting design

Vite is the chosen frontend tooling, with React and TypeScript the preferred
frontend stack and Tailwind CSS 4 for styling. The Vite/React/TypeScript
dashboard proxies API requests during development and builds external static
assets for Spring to serve. Settings, inventory and queue screens are implemented.
Gradle builds the Kotlin/Spring Boot backend. The Elide Gradle plugin is built
from pinned source and supplies formatting and managed runtime provisioning.
Spring Web, PostgreSQL, Liquibase, validation, and Kotlin JSON support are included
in the scaffold, with JUnit/Testcontainers for tests and opt-in Spring DevTools.
See [backend build notes](backend-build.md) for dependency versions.

Defer application login and authorization for this initial phase. Revisit them
with Spring Security later. SSH authentication to the seedbox is still required.

Have the scanner return a versioned JSON inventory through SSH standard output,
including relative paths, file types, byte sizes, and modification times.
Spring can deserialize that inventory into typed Kotlin data classes. This follows
[SeedSync's remote scanner pattern](https://github.com/ipsingh06/seedsync/blob/ff2a1039935beccbbf7ec76134b41d2e91137742/src/python/controller/scan/remote_scanner.py),
using JSON in place of Python pickle. It requires SSH command access and Python
on the seedbox; no seedbox HTTP service or cron job is needed.

Start with one seedbox and one active download. Persist transfer state in
PostgreSQL and run downloads as background jobs within Spring. Use browser HTTP polling every two seconds for dashboard updates, independently
of scheduled remote filesystem discovery.

Configuration should cover SSH host, port, username and authentication, remote
source directory, local download directory, scan interval, and temporary-file
mode. Verify SSH host keys, keep credentials out of logs and browser responses,
and constrain downloaded paths to the configured destination.

## First milestone

- [x] Establish a local SSH test container with Python and a sample directory tree.
- [x] Verify the Elide build workflow for the Kotlin/Spring Boot backend.
- [x] Connect from the backend, upload the scanner when needed, and execute it.
- [x] Return a typed inventory, including nested paths, sizes, and timestamps.
- [x] Verify names containing spaces and Unicode, an empty directory, and clear
      errors for an inaccessible source or failed SSH connection.

Acceptance: given SSH credentials and a remote directory, SporeSync returns an
accurate inventory without requiring a separately managed seedbox service.

The remaining slices below are implemented and verified. Their original feature
boundaries and dependencies are retained as a guide to the stacked PRs.

## Resolved behavior

Defaults are temporary files enabled and five-minute scans. Preserve subfolders.
Recommend a completed-download source directory and require two unchanged scans
for automatic eligibility. Verify all existing bytes before appending; equal files
require full comparison, and smaller/replaced remote content fails without truncation.
Existing final files resume in place even in temporary mode. Cancellation retains
partials; retry is explicit after cancellation or three failed attempts. Deploy a
`sporesync.jar` and static assets with Java 25 on the host; see [deployment](deployment.md).

## Implementation slices

Keep implementation PRs around 200–400 changed lines, each with one reviewable
outcome. Split a slice further if needed; these are feature boundaries, not a
commitment to finish an entire feature in one PR. The initial reset is exempt.

1. **Remote scanner contract and SSH fixture** (implemented first): versioned
   JSON, nested files, empty directories, Unicode/space-containing names, and
   explicit filesystem failures. Report symlinks without following them. Exercise
   scanner upload and execution against a disposable SSH/Python container.
2. **Elide/Spring feasibility**: verify the current documented Elide workflow with
   a minimal Kotlin/Spring Web application, selecting compatible JVM and dependency
   versions. Demonstrate build, run, and a focused automated test. Record exact
   commands and constraints; if incompatible, resolve the tool choice before
   generating the application scaffold. This gates Kotlin/JVM implementation.
3. **Backend remote inventory**: typed versioned data classes, pinned SSH host keys,
   credential-safe configuration, scanner upload/version checks, execution
   timeout, and clear connection/protocol errors. Use the SSH fixture for the
   first milestone's acceptance test. Reject unsupported inventory versions.
4. **One background transfer**: manually request one discovered regular file,
   preserve subfolders, constrain the destination path, and expose byte progress
   independently of browser lifetime. Verify content through real SFTP.
5. **Temporary files and resume**: cover both filename modes and remote growth.
   Resolve replacement detection, equal/smaller files, and existing final files
   before enabling automatic resume. Never assume size proves matching content.
6. **Durable transfer lifecycle**: PostgreSQL/Liquibase state, explicit completion,
   restart recovery, bounded retries, and cancellation. Verify restart behavior
   without duplicate workers writing the same destination.
7. **Scheduled discovery and automatic queue**: one seedbox, one active download,
   automatic downloading enabled by default, configurable interval/source, and
   deduplication across scans. Resolve changing-file eligibility and whether the
   source should be a torrent client's completed directory.
8. **Settings APIs and dashboard**: expose validated backend settings APIs, verify
   Vite setup, then add React/TypeScript
   settings, inventory, queue state, and progress in separate small PRs. Choose
   SSE or polling when the backend progress contract exists. Never return SSH
   credentials to the browser.
9. **Packaging**: document persistent storage, configuration, startup/recovery,
   and host-key provisioning. Application login remains deferred as agreed.

### Dependencies and independent work

Slice numbers identify scope, not a strictly sequential schedule. The initial
scaffold was limited to slice 2: a verified Elide/Spring build, a minimal web
endpoint, and HTTP/PostgreSQL integration tests. It includes database dependencies
and Liquibase setup. A subsequent persistence foundation adds Spring Data JPA,
the `sporesync_settings` table, and typed conversion of string values. SSH,
transfers, scheduling, and settings APIs were implemented in the dependent slices.

| Slice | Prerequisites | Work that can proceed independently |
|-------|---------------|-------------------------------------|
| 1. Scanner and SSH fixture | None; implemented | Independent of the Kotlin scaffold. |
| 2. Elide/Spring scaffold | Verify Elide compatibility before scaffolding | Independent of slice 1; gates all Kotlin backend implementation. |
| 3. Backend inventory | 1 and 2 | Establish the inventory API for dashboard work. |
| 4. Background transfer | 3, including trusted SSH configuration and discovered regular files | Define transfer state and progress contracts for slices 6 and 8. |
| 5. Temporary files and resume | 4; resolve replacement and existing-file policies | Can develop alongside slice 6 using an agreed transfer state model. |
| 6. Durable lifecycle | 4 and a defined transfer state model | Persistence can start before 5 is finished; complete restart/resume integration after 5. |
| 7. Scheduled discovery and automatic queue | 3, 5, and 6; resolve changing-file eligibility | Scheduling can be built earlier, but automatic downloads wait for safe resume and durable recovery. |
| 8a. Settings APIs | 2 and defined configuration fields, validation, and defaults | Develop alongside the relevant SSH, transfer, and scheduling slices; each setting needs its consuming feature for end-to-end verification. |
| 8b. Dashboard | Stable APIs for each screen: 3 for inventory, 4 for progress, 7 for queue, 8a for settings | Verify Vite and build screens incrementally; choose SSE or polling once the progress contract exists. |
| 9. Packaging | Verified startup, storage, configuration, and recovery behavior for the included features | Draft deployment documentation earlier; verify the complete application after integration. |

The main backend dependency chain is **2 → 3 → 4 → (5 and 6) → 7**, with
slice 1 also required by slice 3. Settings APIs branch from slice 2 as their
contracts are defined; dashboard screens branch from their respective APIs.
Independent work is optional, not a requirement to use multiple agents or PRs
at once. Keep each PR focused and integrate shared contracts before dependent
changes.

### Current milestone status

The first milestone is implemented and verified through the backend HTTP endpoint
against disposable SSH and PostgreSQL containers. SSHJ verifies known host keys,
authenticates with a private key, uploads scanner versions over SFTP, and executes
bounded scans. Kotlin inventory validation rejects unsupported schemas and invalid
paths or missing metadata. See the README for configuration and
[backend build notes](backend-build.md) for the Gradle workflow.
SSH host, port, username, source directory, and timeout are database-backed
application settings; migrations seed port and timeout without overwriting values.
Credentials and host-key trust remain external configuration.
All planned implementation slices are now implemented in the stacked PRs below.
Application login remains explicitly deferred. Verification includes 50 backend
integration tests, 9 scanner/SSH tests, strict TypeScript/Vite builds and real
process-kill recovery. Browser visual verification was unavailable in this environment.

| PR | Outcome |
|----|---------|
| #63–64 | Backend SSH inventory and database-backed connection settings |
| #65 | Validated settings API and defaults |
| #66 | Durable queue, progress and lifecycle storage |
| #67 | SFTP, content-verified resume and path confinement |
| #68 | Background worker, cancellation, retry and exclusive ownership |
| #69 | Scheduled discovery and automatic stable-file queue |
| #70–71 | Settings, inventory and download dashboard |
| #72–73 | Superseded: fixes folded into #65–70; acceptance script moved to #74 |
| #74 | Deployment bundle, operational docs and process-crash acceptance check |
| #75 | Optional password authentication, branching from #67 |

Backend order is #65 → #66 → #67 → #68 → #69, starting from main (#64 merged).
Settings UI #70 branches from #65; authentication #75 branches from #67.
Dashboard #71 joins #69 and #70, and packaging #74 follows #71.
Merge #70 before #71; its changes are included in #71 until then. Retarget each
PR to main once its prerequisites merge. #75 can merge independently of the UI.

### Implemented policies

Use five-minute scans and temporary files by default. Preserve subdirectories.
Use a completed-download directory when available; automatic queuing
requires unchanged size and modification time in two consecutive scans. Compare
existing bytes with the remote prefix before appending. Equal files require full
comparison; smaller or replaced content fails safely without truncating local
files. Cancellation retains partial data and retry is explicit after bounded
failures. Browser progress uses polling. Deploy the Spring Boot JAR and Vite
assets together with external PostgreSQL and persistent downloads/SSH material.
