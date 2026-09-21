# SporeSync

SporeSync is being rebuilt around the [starting brief](docs/plan.md).
The implementation includes a dependency-free Python remote scanner and a
Kotlin/Spring Boot backend with SSH inventory discovery and a minimal
Vite/React/TypeScript frontend. Downloads and dashboard features are subsequent
slices in the plan.

## Source layout

Backend Kotlin code uses `model/` for domain types, persistence, and business
logic, `controller/` for HTTP endpoints, and `config/` for runtime settings.
The React frontend supplies the view: `model/` contains typed contracts,
`view/` contains components and styles, `controller/` contains hooks and API
requests, and `config/` contains shared endpoint and polling constants.
Entry points and tool manifests stay at their standard locations; runtime
backend configuration stays in `backend/config/`. The remote scanner remains
one independently uploadable script.
Use interfaces for I/O-facing service contracts (scanning, file transfer, job
persistence, and settings storage); keep immutable data models concrete.



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

Configure the database as above. Liquibase seeds `ssh.port = 22` and
`ssh.timeout.millis = 30000` in `sporesync_settings`, preserving existing values.
A follow-up migration seeds empty host, username, and source rows without
overwriting configured values. Fill these required values through `ApplicationSettings`
using `SshSettingKeys`, or run this SQL against the configured database:

```sql
INSERT INTO sporesync_settings (name, value) VALUES
  ('ssh.host', 'seedbox.example.com'),
  ('ssh.username', 'scanner'),
  ('remote.source.directory', '/absolute/remote/source')
ON CONFLICT (name) DO UPDATE
SET value = EXCLUDED.value, updated_at = CURRENT_TIMESTAMP;
```

Each scan reads these five database settings once; changes apply to the next scan
without restarting. Missing or invalid values return `CONFIGURATION`. Port must
be between 1 and 65535; timeout is a positive integer in milliseconds. Existing
`SPORESYNC_SSH_*` environment values for host, port, username, source, and timeout
must be moved to their database rows; those environment overrides are no longer
used.

Credentials, host trust, and the local scanner path remain external configuration.
Set these environment variables before running `elide run` from `backend/`:

```bash
export SPORESYNC_SSH_AUTHENTICATION=KEY # Default; existing key setups still work.
export SPORESYNC_SSH_PRIVATEKEY=/absolute/path/to/private-key
export SPORESYNC_SSH_KNOWNHOSTS=/absolute/path/to/known_hosts
```

For SSH account password login, set `SPORESYNC_SSH_AUTHENTICATION=PASSWORD`
and supply `SPORESYNC_SSH_PASSWORD` through your service's secret/environment
mechanism. No private-key file or key passphrase is required in password mode.
`KEY` requires a private-key file; `PASSWORD` requires a nonempty password, used
exactly as supplied. Only the selected method is attempted, with no fallback.
This supports SSH password authentication, not interactive MFA challenges.
Restart the backend after changing authentication or credentials. The same choice
applies to manual/scheduled scans and background SFTP downloads. Authentication
and credentials remain external configuration, outside the dashboard settings API
and database job snapshots.

The known-hosts file is required in both modes and must contain a host key verified
through a trusted channel; unknown or changed keys are rejected. Optional settings are
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
Failures return HTTP 502 with an `error` category: `CONFIGURATION`, `CONNECTION`
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

```kotlin
val scanInterval = SettingKey(SettingNames.SCAN_INTERVAL, Duration::parse, Duration::toString)
settings.set(scanInterval, Duration.ofMinutes(5))
val interval: Duration? = settings.get(scanInterval)
```

Search `SettingNames.kt` for application setting names. These constants reserve
names for settings. `SshSettingKeys` defines the typed keys used by inventory;
other reserved names do not enable planned features.

This is an example, not a configured default. Declare each real key once alongside
its consuming feature. Missing values return `null`; malformed values propagate
parser errors. Use strict parsers (such as `String::toBooleanStrict`) to reject
invalid input. Settings are application-wide; no user accounts or settings HTTP
API are introduced yet.

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
`frontend/src/view/styles.css` imports Tailwind, and React components use utility classes
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
These files are not embedded in a JAR; Elide packaging remains deferred.
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
The scanner holds the inventory in memory before publishing it. Transfer-time
validation and protection against concurrent directory replacement belong to
later work; this scanner is intended for a trusted seedbox directory.

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
