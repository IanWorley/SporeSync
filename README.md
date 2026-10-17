# SporeSync

A self-hosted tool that mirrors files from a remote seedbox to a local machine over SSH, with a web UI.
It is a rewrite of [seedsync](https://github.com/nitrobass24/seedsync). See [docs/SPEC.md](docs/SPEC.md).

## Requirements

[Elide](https://elide.dev) bundles the JDK and Kotlin compiler and builds the backend. Gradle and Maven are
not used.

```bash
curl -sSL --tlsv1.2 elide.sh | bash -s -- --channel=release --install-rev=1.6.0
```

The Elide version is pinned in [`.elideversion`](.elideversion).

For now the frontend uses [Vite+](https://viteplus.dev) (`vp`) and [bun](https://bun.sh) instead of Elide,
because Elide 1.6.0 cannot yet run Vite or Vitest. `vp` installs the Node.js version in
[`frontend/.node-version`](frontend/.node-version) and the bun version in `frontend/package.json`, so the only
thing to install by hand is `vp`:

```bash
curl -fsSL https://vite.plus | bash
```

## Layout

| Path | Contents |
| --- | --- |
| `elide.pkl` | Elide workspace root |
| `backend/` | Kotlin, Spring Boot, Spring Modulith application |
| `tools/` | ktlint and detekt CLIs, run on Elide's JVM |
| `frontend/` | TypeScript React app: Vite+, TanStack Router, TanStack Query, Tailwind |
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
vp install        # install npm packages with bun
vp dev            # dev server, proxies /api to the backend on :8080
vp build          # build the static bundle into dist/
vp test           # Vitest
vp fmt --check    # Oxfmt, check only
vp fmt            # Oxfmt, fix in place
vp lint           # Oxlint with type-aware rules and a full TypeScript type-check
```

## Logging

Logs are structured JSON (Elastic Common Schema), one object per line on stdout. To turn on debug logging
for SporeSync's own code:

```bash
LOGGING_LEVEL_COM_SPORESYNC=DEBUG elide run
```
