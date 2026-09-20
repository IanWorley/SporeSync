# SporeSync

SporeSync is being rebuilt around the [starting brief](docs/plan.md).
The implementation includes a dependency-free Python remote scanner and a
Kotlin/Spring Boot backend scaffold. Backend SSH discovery, downloads, and the
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

Tests start disposable PostgreSQL through Testcontainers and verify HTTP/JSON
and Liquibase initialization. Docker is required; tests do not silently skip.

To run against your own PostgreSQL database, provide `SPRING_DATASOURCE_URL`,
`SPRING_DATASOURCE_USERNAME`, and `SPRING_DATASOURCE_PASSWORD` in the environment,
then run `elide run` from `backend/`. Do not put credentials in tracked files.
The server binds to loopback by default; `GET http://127.0.0.1:8080/api/status`
returns `{"application":"sporesync"}`. `SERVER_PORT` overrides the default port.

For development, run `elide run -fDEV`; in another terminal run
`elide build -fDEV` after editing Kotlin. DevTools watches compiled classes,
not source files. It is excluded unless the `DEV` build flag is set.
See [backend build notes](docs/backend-build.md) for versions and limitations.

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
