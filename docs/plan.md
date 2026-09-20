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
- Copy one way from the seedbox to the host, retaining source files. Do not
  propagate deletions or upload local changes.
- When a remote file is larger than the existing local file, resume downloading
  from the local byte count and append the remaining bytes. In temporary-file
  mode, apply the same behavior to the partial download. Size alone does not
  establish that an existing local file is a matching prefix of the remote file;
  handling replaced files remains an implementation decision.
- Make temporary `.part` files optional: when enabled, download to the temporary
  name and rename after completion; when disabled, write to the final filename.
  Track completion independently of the filename in both modes.

## Proposed starting design

Vite+ is the chosen frontend tooling, with React and TypeScript the preferred
frontend stack and Tailwind proposed for styling. Elide is the requested build
tool; verify its build workflow and compatibility with the proposed Java/Spring
Boot application before scaffolding. Do not silently substitute Maven or Gradle.
Spring Web, PostgreSQL, and Liquibase remain the backend direction. Dependency
versions are not yet selected.

Defer application login and authorization for this initial phase. Revisit them
with Spring Security later. SSH authentication to the seedbox is still required.

Have the scanner return a versioned JSON inventory through SSH standard output,
including relative paths, file types, byte sizes, and modification times.
Spring can deserialize that inventory into typed records. This follows
[SeedSync's remote scanner pattern](https://github.com/ipsingh06/seedsync/blob/ff2a1039935beccbbf7ec76134b41d2e91137742/src/python/controller/scan/remote_scanner.py),
using JSON in place of Python pickle. It requires SSH command access and Python
on the seedbox; no seedbox HTTP service or cron job is needed.

Start with one seedbox and one active download. Persist transfer state in
PostgreSQL and run downloads as background jobs within Spring. Use server-sent
events for dashboard updates as a proposed choice; browser HTTP polling is also
viable and is separate from remote filesystem discovery.

Configuration should cover SSH host, port, username and authentication, remote
source directory, local download directory, scan interval, and temporary-file
mode. Verify SSH host keys, keep credentials out of logs and browser responses,
and constrain downloaded paths to the configured destination.

## First milestone

- [x] Establish a local SSH test container with Python and a sample directory tree.
- [ ] Verify the Elide build workflow for the Spring Boot backend.
- [ ] Connect from the backend, upload the scanner when needed, and execute it.
- [ ] Return a typed inventory, including nested paths, sizes, and timestamps.
- [ ] Verify names containing spaces and Unicode, an empty directory, and clear
      errors for an inaccessible source or failed SSH connection.

Acceptance: given SSH credentials and a remote directory, SporeSync returns an
accurate inventory without requiring a separately managed seedbox service.

Next, download one file with measurable progress in both temporary-file modes,
including resuming when the remote file is larger. Then add persistent job state,
restart handling and automatic queuing enabled by default,
followed by the settings and transfer dashboard.

## Choices to resolve as we reach them

- Verify Elide integration and select supported dependency versions.
- Choose the default for temporary-file mode and the scan interval.
- Decide whether to scan only a torrent client's completed-download directory.
- Define behavior for equal-size files, smaller remote files, replaced content,
  and files changing during a transfer. Also define how an existing final file
  resumes when temporary-file mode is enabled, plus cancellation and retry.
- Confirm subfolder preservation and deployment packaging.

## Implementation slices

Keep implementation PRs around 200–400 changed lines, each with one reviewable
outcome. Split a slice further if needed; these are feature boundaries, not a
commitment to finish an entire feature in one PR. The initial reset is exempt.

1. **Remote scanner contract and SSH fixture** (implemented first): versioned
   JSON, nested files, empty directories, Unicode/space-containing names, and
   explicit filesystem failures. Report symlinks without following them. Exercise
   scanner upload and execution against a disposable SSH/Python container.
2. **Elide/Spring feasibility**: verify the current documented Elide workflow with
   a minimal Spring Web application, selecting compatible Java and dependency
   versions. Demonstrate build, run, and a focused automated test. Record exact
   commands and constraints; if incompatible, resolve the tool choice before
   generating the application scaffold. This gates Java implementation.
3. **Backend remote inventory**: typed versioned records, pinned SSH host keys,
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
8. **Settings and dashboard**: verify Vite+ setup, then add React/TypeScript
   settings, inventory, queue state, and progress in separate small PRs. Choose
   SSE or polling when the backend progress contract exists. Never return SSH
   credentials to the browser.
9. **Packaging**: document persistent storage, configuration, startup/recovery,
   and host-key provisioning. Application login remains deferred as agreed.

### Current milestone status

The scanner and disposable SSH fixture are implemented. The fixture exercises
client-side upload and execution; it is not the planned Spring integration.
Elide verification, typed Java inventory, backend connection errors, and the
full first-milestone acceptance test remain pending. No transfer behavior or
unresolved resume policy is implied by the scanner implementation.
