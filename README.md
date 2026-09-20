# SporeSync

SporeSync is being rebuilt around the [starting brief](docs/plan.md).
The implementation includes a dependency-free Python remote scanner and a
Kotlin/Spring Boot backend scaffold and a minimal Vite/React/TypeScript frontend.
Backend SSH discovery, downloads, and dashboard features are subsequent slices.

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

Tests start disposable PostgreSQL through Testcontainers and verify HTTP/JSON
and Liquibase migrations plus JPA settings persistence and typed conversion. Docker is required; tests do not silently skip.

To run against your own PostgreSQL database, provide `SPRING_DATASOURCE_URL`,
`SPRING_DATASOURCE_USERNAME`, and `SPRING_DATASOURCE_PASSWORD` in the environment,
then run `elide run` from `backend/`. Do not put credentials in tracked files.
The server binds to loopback by default; `GET http://127.0.0.1:8080/api/status`
returns `{"application":"sporesync"}`. `SERVER_PORT` overrides the default port.

For development, run `elide run -fDEV`; in another terminal run
`elide build -fDEV` after editing Kotlin. DevTools watches compiled classes,
not source files. It is excluded unless the `DEV` build flag is set.
See [backend build notes](docs/backend-build.md) for versions and limitations.

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
names for planned settings; they do not seed rows or enable features.

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
