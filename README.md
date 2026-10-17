# SporeSync

A self-hosted tool that mirrors files from a remote seedbox to a local machine over SSH, with a web UI.
It is a rewrite of [seedsync](https://github.com/nitrobass24/seedsync). See [docs/SPEC.md](docs/SPEC.md).

## Requirements

[Elide](https://elide.dev) is the only tool you need. It bundles the JDK and Kotlin compiler, builds the
backend, and will run the frontend toolchain. Node.js, Gradle, and Maven are not used.

```bash
curl -sSL --tlsv1.2 elide.sh | bash -s -- --channel=release --install-rev=1.6.0
```

The Elide version is pinned in [`.elideversion`](.elideversion).

## Layout

| Path | Contents |
| --- | --- |
| `elide.pkl` | Elide workspace root |
| `backend/` | Kotlin, Spring Boot, Spring Modulith application |
| `tools/` | ktlint and detekt CLIs, run on Elide's JVM |
| `docs/` | Specification |

The `frontend/` member (TypeScript React on Vite, run by Elide) is added in a later issue.

## Build and run

From the repository root:

```bash
elide install     # resolve Maven dependencies for every member
elide build       # compile
elide test        # run JUnit tests
```

Run the backend from `backend/`:

```bash
cd backend
elide run
```

The server listens on <http://localhost:8080>. Check it with:

```bash
curl http://localhost:8080/actuator/health   # {"groups":["liveness","readiness"],"status":"UP"}
```

## Format and lint

From `backend/`. Each command exits non-zero on a violation.

```bash
elide run format-check   # ktlint, check only
elide run format         # ktlint, fix in place
elide run lint           # detekt
```

## Logging

Logs are structured JSON (Elastic Common Schema), one object per line on stdout. To turn on debug logging
for SporeSync's own code:

```bash
LOGGING_LEVEL_COM_SPORESYNC=DEBUG elide run
```
