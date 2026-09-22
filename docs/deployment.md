# Deploy SporeSync

The release contains the backend JAR, configuration, scanner, built frontend
assets, and startup script. The target host runs the JAR with Java 25. It does
not compile Kotlin or need Gradle, Elide, dependency caches, or backend source.

## Build a release

From a checkout, install Java 17, Temurin Java 25, and Node 22.12+ (24
recommended). Set `JAVA_HOME` to Java 25, then run:

```bash
scripts/package.sh /absolute/output/directory
```

This runs frontend `npm ci`/`npm run build` and backend `./gradlew bootJar`, then
writes `sporesync-<commit>.tar.gz`. The archive contains
`backend/build/libs/sporesync.jar`, configuration, scanner, frontend assets,
`scripts/start.sh`, docs, and the README. It refuses to overwrite an existing
archive. Generated assets, caches, and release archives must not be committed.
Run the required tests before shipping; packaging does not claim to run them.

## Prepare the host

Extract the archive into a new release directory. Keep PostgreSQL data, downloads,
private keys and known-hosts files outside that directory so upgrades preserve them.
Install Java 25 on the target. Set `JAVA_HOME` to that installation, or make its
`java` executable available on `PATH`. The packaged JAR is
`backend/build/libs/sporesync.jar`. The target does not need Java 17, Gradle,
Elide, dependency caches, network access for dependency resolution, or a writable
build directory. Use a dedicated unprivileged account with write access to the
download root and read access to SSH material. The seedbox requires Python 3.9+,
SFTP, shell command access, and a writable home. PostgreSQL 17 is the verified
database version.

Set these variables in the service environment, never in tracked files:

```bash
export SPRING_DATASOURCE_URL=jdbc:postgresql://127.0.0.1:5432/sporesync
export SPRING_DATASOURCE_USERNAME=sporesync
# Set SPRING_DATASOURCE_PASSWORD through your service's secret/environment mechanism.
export SPORESYNC_SSH_PRIVATEKEY=/srv/sporesync/ssh/id_ed25519
export SPORESYNC_SSH_KNOWNHOSTS=/srv/sporesync/ssh/known_hosts
# Optional for encrypted private keys: SPORESYNC_SSH_PASSPHRASE
```

For password authentication, set
`SPORESYNC_SSH_AUTHENTICATION=PASSWORD` and inject `SPORESYNC_SSH_PASSWORD`
through the service's secret mechanism instead of supplying a private key.
KEY remains the default. Both modes require known-host verification and never
fall back to another method. Restart after changing credentials. The selection
applies to scans and SFTP; credentials stay outside database settings and job
snapshots. Interactive MFA is not supported. See README for complete setup.

Provision the known-hosts entry only after checking its fingerprint against the
seedbox provider or another trusted channel. `ssh-keyscan` can collect a candidate
key but does not authenticate it. Non-default ports use `[hostname]:port` entries.
Keep private keys readable only by the runtime account. Unknown or changed host
keys fail closed; there is no trust-on-first-use option.

## Start and configure

Run `scripts/start.sh` from anywhere; it selects the backend working directory
and executes `java -jar backend/build/libs/sporesync.jar`. The script uses
`$JAVA_HOME/bin/java` when `JAVA_HOME` is set, otherwise it uses `java` from
`PATH`. A supervisor can signal the Java process directly. Set its shutdown
timeout above the maximum configured SSH timeout.

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
- Temporary downloads publish through an atomic, non-replacing hard link after a
  filesystem support probe. Existing final files resume in place. Remote SHA-256
  checks verify local prefixes without truncation. Cancellation retains partials.
- The seedbox hashes content before and after transfer; only the missing suffix
  travels over SFTP. Use trusted directories without concurrent local edits.
  Discovery uses size/time metadata and cannot detect a completed
  source replaced with identical size and timestamp until a transfer is requested
  under a new source version. A scan is not a filesystem snapshot.
- Interrupted jobs resume after restart, with at most three attempts. Cancellation
  and terminal failures require explicit Retry. Browser closure has no effect.
- Run one deployment per database/download root. PostgreSQL ownership and a local
  file lock prevent concurrent workers; network filesystems must provide reliable
  locks, hard links and directory synchronization. Linux requires `/proc/self/fd`;
  macOS requires `/dev/fd` for descriptor-based metadata.
- Stop the old process before switching release directories, preserve external
  state, then start the new release. Liquibase applies forward migrations at
  startup. Take a database backup before upgrades; do not edit applied migrations.
  Queued jobs retain their original configuration after settings changes.

## Verification

From the development checkout, run `scripts/package.sh` and then
`python3 scripts/verify-runtime.py` with Java 25, Docker, and ssh-keygen. The
runtime probe invokes `scripts/start.sh` and tests production assets, real
SSH/SFTP, automatic queueing, cancellation/retry, and process-crash recovery of
a 128 MiB file with SHA-256 comparison. It uses only disposable fixtures. The
backend suite additionally verifies PostgreSQL migrations, path confinement,
remote replacement, stable scans, and exclusive worker ownership.

Fresh Gradle packaging and runtime validation is pending. Do not treat the
historical Elide release checks as evidence for this release format.

## Run with Docker

Build and verify the image from the repository root. Docker supplies Node 24,
Java 17 and Java 25, so these tools are not required on your host:

```bash
docker build -t sporesync:verify .
bash scripts/verify-container.sh sporesync:verify
```

The verification script requires Bash, curl, Python 3 and Docker. It starts an
isolated PostgreSQL 17 instance and the real application image, then checks the
dashboard HTML, JavaScript, CSS and database-backed settings API over HTTP.
Its exit trap removes only its own containers and network.

For deployment, create an environment file outside the checkout with
`SPRING_DATASOURCE_URL`, `SPRING_DATASOURCE_USERNAME`, and
`SPRING_DATASOURCE_PASSWORD`. Use a database hostname reachable from the
container; `127.0.0.1` inside Docker refers to the application container.
Add the SSH environment variables described above, with paths under `/ssh`.
Prepare a downloads directory writable by UID 10001 and SSH files readable by
that UID. Replace the version and host paths below with your own:

```bash
docker run --detach --name sporesync --restart unless-stopped \
  --publish 127.0.0.1:8080:8080 \
  --env-file /srv/sporesync/runtime.env \
  --mount type=bind,src=/srv/sporesync/downloads,dst=/downloads \
  --mount type=bind,src=/srv/sporesync/ssh,dst=/ssh,readonly \
  ghcr.io/ianworley/sporesync:v0.1.0
```

Open `http://127.0.0.1:8080` and set the download destination to `/downloads`.
Spring serves both the frontend and API on port 8080. The image binds to all
container interfaces; the example publishes the port only on host loopback.
Keep PostgreSQL, downloads and SSH material outside the image. The runtime runs
as UID 10001 and includes the scanner and Liquibase migrations.

Push a Git tag to build and publish an image to GitHub Packages. The workflow
runs the existing CI checks and container verification before publishing. Each
image uses the exact Git tag; tags must be valid Docker tags. It does not update
`latest`. CI publishes Linux amd64 images; local builds use the Docker host's
architecture. Package visibility is managed in GitHub Packages settings.
