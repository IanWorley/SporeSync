# SporeSync

A self-hosted tool that mirrors files from a remote seedbox to a local machine over SSH, with a web UI.
It is a rewrite of [seedsync](https://github.com/nitrobass24/seedsync). See [docs/SPEC.md](docs/SPEC.md).

## Requirements

[Elide](https://elide.dev) bundles the JDK and Kotlin compiler and builds the backend. Gradle and Maven are
not used.

The frontend also needs [Node.js](https://nodejs.org), at the version in [`frontend/.nvmrc`](frontend/.nvmrc).
Elide installs its npm packages, but Elide 1.6.0 cannot yet run Vite, Vitest, ESLint, or Prettier, so
`elide run` hands those scripts to Node.

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
| `frontend/` | TypeScript React app: Vite, TanStack Router, TanStack Query, Tailwind |
| `docs/` | Specification |

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

## Frontend

From `frontend/`. The check commands exit non-zero on a violation.

```bash
elide install --ecosystems=npm   # install npm packages
elide run dev                    # Vite dev server, proxies /api to the backend on :8080
elide run build                  # type-check and build the static bundle into dist/
elide run test                   # Vitest
elide run format-check           # Prettier, check only
elide run format                 # Prettier, fix in place
elide run lint                   # TypeScript type-check, then ESLint
```

## Logging

Logs are structured JSON (Elastic Common Schema), one object per line on stdout. To turn on debug logging
for SporeSync's own code:

```bash
LOGGING_LEVEL_COM_SPORESYNC=DEBUG elide run
```
