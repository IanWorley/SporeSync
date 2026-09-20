# Deploy SporeSync

The release is a source-and-static-assets tarball. Elide compiles Kotlin on the
target host and supplies its bundled JVM; no Maven, Gradle, standalone JDK, Node
or npm is needed at runtime. This avoids bundling platform-specific Elide caches
or pretending that compiled classes are an executable JAR. Use the verified
Elide build from [backend-build.md](backend-build.md) on a supported host.

## Build a release

From a checkout, install Elide and Node 22.12+ (24 recommended), then run:

```bash
scripts/package.sh /absolute/output/directory
```

This runs frontend `npm ci`/`npm run build` and backend `elide build`, then writes
`sporesync-<commit>.tar.gz`. It refuses to overwrite an existing archive. Generated
assets, caches and release archives must not be committed. Run the required tests
before shipping; packaging does not claim to run them.

## Prepare the host

Extract the archive into a new release directory. Keep PostgreSQL data, downloads,
private keys and known-hosts files outside that directory so upgrades preserve them.
Install the same verified Elide runtime on the target and put it on PATH (or set
`ELIDE_BIN` to its absolute path). From the extracted `backend/` directory run:

```bash
elide install --slim
elide build
```

Dependency resolution requires network access on first installation. The runtime
user needs a writable Elide cache and build directory. Use a dedicated unprivileged
account with write access to the download root and read access to SSH material.
The seedbox requires Python 3.9+, SFTP, shell command access and a writable home.
PostgreSQL 17 is the verified database version.

Set these variables in the service environment, never in tracked files:

```bash
export SPRING_DATASOURCE_URL=jdbc:postgresql://127.0.0.1:5432/sporesync
export SPRING_DATASOURCE_USERNAME=sporesync
# Set SPRING_DATASOURCE_PASSWORD through your service's secret/environment mechanism.
export SPORESYNC_SSH_AUTHENTICATION=KEY # Default
export SPORESYNC_SSH_PRIVATEKEY=/srv/sporesync/ssh/id_ed25519
export SPORESYNC_SSH_KNOWNHOSTS=/srv/sporesync/ssh/known_hosts
# Optional for encrypted private keys: SPORESYNC_SSH_PASSPHRASE
```

Alternatively, set `SPORESYNC_SSH_AUTHENTICATION=PASSWORD` and inject the account
password as `SPORESYNC_SSH_PASSWORD` through the service's secret mechanism.
Password mode does not require a key file. Both modes require the known-hosts
file; neither falls back to another authentication method. Restart the backend
after changing these variables. The choice covers scanning and SFTP transfers;
passwords are never stored in database settings or job snapshots. Interactive MFA
is not supported.

Provision the known-hosts entry only after checking its fingerprint against the
seedbox provider or another trusted channel. `ssh-keyscan` can collect a candidate
key but does not authenticate it. Non-default ports use `[hostname]:port` entries.
Keep private keys readable only by the runtime account. Unknown or changed host
keys fail closed; there is no trust-on-first-use option.

## Start and configure

Run `scripts/start.sh` from anywhere; it establishes the correct backend working
directory before starting Elide. A service supervisor can invoke this absolute
script path, inject the variables above, and restart the process on failure.
Allow at least the configured SSH timeout for graceful shutdown.

Open `http://127.0.0.1:8080`, select Settings, and save host/port/user, absolute
source/destination directories and download preferences. Use your torrent client's
completed-download directory when possible. SSH credentials are never sent to
or returned by the browser. `SERVER_PORT` overrides port 8080. The server binds
to loopback; application login is deferred, so keep access behind a trusted local
connection or authenticated reverse proxy. The API can change paths and queue
transfers and must not be exposed publicly without access control.

Defaults: five-minute scans, automatic downloads on, temporary files on, 30-second
SSH timeout. Files unchanged in two successful scans are automatically queued.
One worker owns the durable queue. Scanning runs independently of active transfers.
Dashboard progress polls every two seconds; closing it does not stop jobs.

## Storage, recovery and upgrades

- PostgreSQL owns settings, job snapshots, progress, attempt counts and completion.
  Back it up together with the download directory and keep them paired.
- `.sporesync/` under the download root holds path-keyed partials and the worker
  lock. Retain it during upgrades. Source paths beginning with that name are reserved.
- New temporary downloads are atomically renamed. Existing final files resume in
  place in either mode. Local prefixes are compared byte for byte; conflicts fail
  without truncation. Cancellation retains partial data; Retry reuses it.
- Each transfer rereads remote content before completion, trading additional
  bandwidth for replacement detection. Use trusted directories without concurrent
  local edits. Discovery uses size/time metadata and cannot detect a completed
  source replaced with identical size and timestamp until a transfer is requested
  under a new source version. A scan is not a filesystem snapshot.
- Interrupted jobs resume after restart, with at most three attempts. Cancellation
  and terminal failures require explicit Retry. Browser closure has no effect.
- Run one deployment per database/download root. PostgreSQL ownership and a local
  file lock prevent concurrent workers; network filesystems must provide reliable
  locks and atomic rename semantics.
- Stop the old process before switching release directories, preserve external
  state, then start the new release. Liquibase applies forward migrations at
  startup. Take a database backup before upgrades; do not edit applied migrations.
  Queued jobs retain their original configuration after settings changes.

## Verification

From the development checkout, build frontend and backend, then run
`python3 scripts/verify-runtime.py` with Elide on PATH, Docker and ssh-keygen.
It tests production assets, real SSH/SFTP, automatic queueing, cancellation/retry
and process-kill recovery of a 128 MiB file with SHA-256 comparison. It uses only
disposable fixtures. The backend suite additionally verifies PostgreSQL migrations,
path confinement, remote replacement, stable scans and exclusive worker ownership.

Verified on 2026-09-20: the extracted archive resolved Elide dependencies, built,
started through `scripts/start.sh` from another working directory, served the real
frontend assets and `/api/status`, retained existing jobs, and completed a new
SFTP download against disposable PostgreSQL/SSH containers. This verification
used macOS arm64; other host platforms require their own compatible Elide runtime.
