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

## SSH host keys

SporeSync connects only to a seedbox whose host key is in an OpenSSH `known_hosts` file, by default
`/config/known_hosts` on the config volume. An unknown or changed key fails the transfer, and the reason
names the file. Set `SPORESYNC_KNOWN_HOSTS_FILE` to use another path. To trust a server, append its key and
check the fingerprint against one from the seedbox provider. Set `SSH_PORT` to the port configured in
`connection.port` before running the scan:

```bash
ssh-keyscan -p "$SSH_PORT" seedbox.example.com >> /config/known_hosts
ssh-keygen -lf /config/known_hosts
```

You can also copy the matching lines from your own `~/.ssh/known_hosts`. The file is read on every connection,
so no restart is needed.

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
