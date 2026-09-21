# SporeSync

SporeSync copies files from one seedbox to your local storage over verified SSH/SFTP.
A Kotlin/Spring Boot backend owns discovery, durable transfers and recovery; the
React/TypeScript dashboard provides settings, inventory and download progress.
Source files stay on the seedbox. See the [implementation plan](docs/plan.md) and
[deployment guide](docs/deployment.md).

## Continuous integration

[Build and test](.github/workflows/ci.yml) runs on every pull request, including
PRs targeting feature branches, on pushes to `main`, and by manual dispatch.
Three independent Ubuntu jobs check:

- Backend dependency installation, `elide build`, Kotlin formatting, and
  `elide test` with disposable PostgreSQL and SSH Testcontainers.
- Frontend `npm ci` and `npm run build`, including TypeScript checking.
- Scanner unit tests and real SSH tests with `SPORESYNC_SSH_TEST=1`.

The runners use Docker directly; missing Docker fails the checks. Tests create
their own temporary credentials and containers, so no seedbox or database secrets
are needed. CI downloads the documented Elide release with a pinned Linux archive
SHA-256 and uses Node 24 and Python 3.12. When upgrading Elide, update the workflow
release/checksum along with `backend/.elideversion` and the backend build notes.
The frontend has no unit-test suite yet; its CI check validates types and builds
production assets. This workflow builds and tests only; it does not deploy.

## Build and test the backend

Install [Elide](https://elide.help/docs/installation) and start Docker. The
backend selects Elide 1.5.3, verified with build `1.5.3+20260917.e2442e4`,
including Java 25 and Kotlin 2.4.20.
No separate Maven, Gradle, or JDK installation is needed.

```bash
cd backend
elide install --slim
elide build
elide test
elide format -- -n src
```

Tests start disposable PostgreSQL and SSH/Python containers through Testcontainers
and verify HTTP inventory, trusted SSH, timeouts, Liquibase migrations, and typed
JPA settings persistence. Docker is required; tests do not silently skip.

To run against your own PostgreSQL database, provide `SPRING_DATASOURCE_URL`,
`SPRING_DATASOURCE_USERNAME`, and `SPRING_DATASOURCE_PASSWORD` in the environment,
then run `elide run` from `backend/`. Do not put credentials in tracked files.
The server binds to loopback by default; `GET http://127.0.0.1:8080/api/status`
returns `{"application":"sporesync"}`. `SERVER_PORT` overrides the default port.

For development, run `elide run -fDEV`; in another terminal run
`elide build -fDEV` after editing Kotlin. DevTools watches compiled classes,
not source files. It is excluded unless the `DEV` build flag is set.
See [backend build notes](docs/backend-build.md) for versions and limitations.

## Scan through the backend

Configure PostgreSQL and the external SSH material below, then open the dashboard's
Settings section. Save host, port, username, source/destination directories, scan
interval and transfer preferences. Liquibase seeds port 22 and timeout 30000 ms;
existing database values are preserved. Connection/download settings live in
`sporesync_settings`; old host/user/source environment overrides are not used.
The SSH timeout is configurable from 1 to 300000 milliseconds and is captured
with each job. Settings changes apply to new scans/jobs without restarting.

Credentials, host trust, and the local scanner path remain external configuration.
Set these environment variables before running `elide run` from `backend/`:

```bash
export SPORESYNC_SSH_PRIVATEKEY=/absolute/path/to/private-key
export SPORESYNC_SSH_KNOWNHOSTS=/absolute/path/to/known_hosts
```

The known-hosts file must contain a host key verified through a trusted channel;
unknown or changed keys are rejected. Optional settings are
`SPORESYNC_SSH_PASSPHRASE` (for encrypted keys) and `SPORESYNC_SSH_SCANNER`
(`../scanner/inventory.py`). Keep credentials outside tracked files.
The seedbox needs Python 3.9+, SFTP, command access, and a writable home directory.

```bash
curl -X POST http://127.0.0.1:8080/api/inventory/scan
```

This synchronous endpoint returns the versioned inventory described below.
Scans are serialized. The backend atomically uploads the scanner into the remote
`~/.sporesync/` directory under a SHA-256 filename and reuses that version on
subsequent scans. Old versions are retained. Output is limited to 16 MiB, stderr
to 64 KiB; SSH operations and command execution have bounded waits.
Invalid configuration returns HTTP 400. Remote failures return HTTP 502 with an `error` category: `CONFIGURATION`, `CONNECTION`
(including host-key rejection), `AUTHENTICATION`, `UPLOAD`, `EXECUTION`, `TIMEOUT`,
or `PROTOCOL`. Remote stderr and credentials are not included in responses.

## Application settings

Liquibase owns the database schema; Hibernate validates its JPA mappings at
startup. Add future migrations under `backend/config/db/changes/` and include
them in `changelog.yaml`. Keep applied changesets unchanged.

`sporesync_settings` stores one application-wide setting per `name` (text primary
key), with a non-null text `value` and `created_at` / `updated_at` timestamps.
Hibernate uses the database clock to populate timestamps and refreshes
`updated_at` on JPA updates while preserving `created_at`. Existing rows receive
the migration time for both timestamps. Spring Data JPA provides persistence through
`ApplicationSettingRepository`. `ApplicationSettings` reads and writes typed
values using a `SettingKey<T>` that pairs a name with parsing and formatting:

The dashboard saves all form fields in one repository transaction. `scan.interval`
is stored as integer seconds, booleans as `true`/`false`, and paths as strings.
`DownloadSettings` provides the typed browser/job contract. Credentials are external.
`download_jobs` stores immutable source/destination snapshots, byte progress,
attempt counts and explicit lifecycle state through JDBC. Liquibase manages both
schemas; generated files and credentials are not stored in Git.

## Frontend development and production assets

Use Node.js 22.12+ (Node 24 recommended) and npm. Install once:

```bash
cd frontend
npm ci
npm run dev
```

Open `http://127.0.0.1:5173`. Run the backend separately from `backend/` with
`elide run -fDEV` and the database environment variables described above.
Vite forwards `/api` and `/api/*` to `http://127.0.0.1:8080`, preserving the path.
React calls relative URLs such as `/api/status`, so no CORS configuration is needed.
Vite handles frontend hot updates; Kotlin still needs `elide build -fDEV`.
To use another backend port, set `BACKEND_URL=http://127.0.0.1:9090 npm run dev`
(and set `SERVER_PORT=9090` for the backend). `BACKEND_URL` is only proxy configuration,
not a browser-exposed variable. It can also go in `frontend/.env.local`.

Tailwind CSS 4 runs through the official `@tailwindcss/vite` plugin.
`frontend/src/styles.css` imports Tailwind, and React components use utility classes
directly. Vite handles CSS hot updates and emits the production stylesheet into
`dist/assets/` for Spring to serve. No separate Tailwind CLI or PostCSS setup is needed.

For a production frontend build:

```bash
cd frontend
npm run build
cd ../backend
elide build
elide run
```

`npm run build` type-checks and writes `frontend/dist/`. Spring serves its
`index.html` at `/` and hashed assets at `/assets/*`; `/api/*` stays on the backend.
Visit `http://127.0.0.1:8080` with Vite stopped to verify this mode.
Elide and Vite remain separate build steps. Generated files are ignored by Git.

When deploying, ship the contents of `frontend/dist/` alongside the backend and
its configuration/dependencies. The default static directory is
`../frontend/dist/` relative to `backend/`. Override it with
`SPORESYNC_STATIC_LOCATION=file:/absolute/path/to/dist/` (include the trailing slash).
These files are not embedded in a JAR. `scripts/package.sh OUTPUT_DIRECTORY`
builds a source-and-assets release; see [deployment](docs/deployment.md).
Rebuild after frontend changes and restart the backend if the build directory
was absent at startup. `npm run preview` previews only the frontend bundle;
use Spring to verify production API integration.

There is no client-side router or deep-link fallback yet. Unknown paths, including
missing API endpoints and assets, return 404. Add explicit UI route handling when
screens need it. Backend tests use temporary static fixtures and do not require
Node or an existing frontend build.

## Run the scanner

Requires Python 3.9 or newer on the scanning machine:

```bash
python3 scanner/inventory.py /absolute/source/directory
python3 -m unittest discover -s tests -v
```

Successful scans write one JSON object to stdout with `schemaVersion: 1` and an
`entries` array. Each entry has a source-relative POSIX `path`, `type` (`file`,
`directory`, `symlink`, or `other`), `sizeBytes`, and `modifiedTimeNs` (integer
nanoseconds since the Unix epoch). Only regular-file sizes describe downloadable
content. The root itself is omitted; empty child directories are included.
Links are reported without traversal. Consumers must only queue regular files.

A filesystem error exits nonzero, writes a diagnostic to stderr, and emits no
JSON. A scan is not a filesystem snapshot: files can change during traversal.
The scanner holds the inventory in memory before publishing it. Transfers verify remote metadata and content and reject symlink destinations.
Use trusted source/destination directories; scans are not filesystem snapshots.

## Verify real SSH upload and execution

Requires a running Docker engine, OpenSSH client, and `ssh-keygen`:

```bash
SPORESYNC_SSH_TEST=1 python3 -m unittest discover -s tests -v
```

The suite builds a disposable Debian SSH/Python container, generates a temporary
client key, binds an ephemeral loopback port, pins the container's SSH host key,
and uploads the scanner over SSH. It verifies nested Unicode filenames and a
permission-denied source. Containers, images, and temporary keys are cleaned up
by the suite. The Debian base follows bookworm updates; it is a test fixture,
not a production deployment image.

## Dashboard settings contract

`GET /api/settings` returns non-secret connection and download settings;
`PUT /api/settings` validates and atomically saves the complete form. The source
and destination must be absolute paths. Defaults are a five-minute scan interval,
automatic downloads enabled, and temporary files enabled. Credentials and trusted
host keys remain external configuration. Settings changes apply to future jobs;
already queued jobs retain their original source and destination.

## Safe transfer policy

New downloads use `.sporesync/<path-hash>.part` under the destination, then an
atomic rename. `.sporesync` is reserved and cannot be downloaded as a source path.
An existing final file resumes in place even when temporary mode is enabled;
it is never moved away or truncated. Every existing byte must match the remote
prefix. Smaller or replaced remote content reports a conflict. Equal-size content
is compared completely. Transfers check remote metadata and reread content before
completion, retaining partial data on cancellation or failure. Keep source and
local download directories under trusted control; do not edit them during a job.

`POST /api/downloads` with `{"path":"nested/file.ext"}` discovers and queues a
regular file. `GET /api/downloads` returns durable state and byte progress;
`POST /api/downloads/{id}/cancel` retains partial data, and `/retry` explicitly
requeues failed or cancelled work. A backend worker processes one file at a time,
independently of the browser. Interrupted jobs recover at startup, with three
attempts maximum. Both a PostgreSQL session lock and destination filesystem lock
protect writes. Run one deployment against a given destination and database.

Automatic discovery runs at the saved interval (five minutes by default). Only
regular files unchanged in two consecutive successful scans enter the automatic
queue. Prefer your torrent client's completed-download directory; stability checks
reduce but cannot eliminate races with active writers. `GET /api/inventory` reports
the last inventory, attempt/success times and a safe error code. Manual scans use
`POST /api/inventory/scan`. Disabling automatic downloads keeps discovery active
and does not cancel already queued jobs. After a restart, two new scans establish
stability; persisted completed/cancelled jobs remain deduplicated.

The dashboard at `/` includes file filtering, manual scan/download actions,
settings, and a durable queue with progress, cancellation and retry. It polls every
two seconds without overlapping requests and leaves transfers running when closed.

## Process recovery acceptance check

After building the frontend and backend, run `python3 scripts/verify-runtime.py`
from the repository root with Elide on PATH. Python 3.9+, Docker and ssh-keygen
are required. It creates disposable PostgreSQL/SSH containers, serves the built
frontend, verifies automatic downloading, cancels and retries a 128 MiB transfer,
kills its own backend process, and checks exact content after restart. All
fixture containers, keys and partial files are cleaned up.
