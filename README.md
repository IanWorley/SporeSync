# SporeSync

SporeSync is being rebuilt around the [starting brief](docs/plan.md).
The implementation includes a dependency-free Python remote scanner and a
Kotlin/Spring Boot backend with SSH inventory discovery. Downloads and the
dashboard are subsequent slices in the plan.

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
Host, username, and source have no defaults. Set them through `ApplicationSettings`
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
`SPORESYNC_SSH_HOST`, `PORT`, `USERNAME`, `SOURCE`, and `TIMEOUTMILLIS` environment
values must be moved to their database rows; those environment overrides are no
longer used.

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
