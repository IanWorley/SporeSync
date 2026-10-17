---
name: verify-sporesync
description: Launch, health-check, drive, capture evidence from, and clean up a real SporeSync backend (Kotlin/Spring Boot on Elide) over HTTP. Use when proving SporeSync behavior in the running app rather than in unit tests.
---

# Verify SporeSync

SporeSync today is a Spring Boot backend run by Elide 1.6.0 (no Node.js). Its only user-facing surface is the operator health check: `GET /actuator/health`, its `liveness` and `readiness` groups, and the `/actuator` index. The frontend, `/api` routes, and every other feature in `docs/SPEC.md` are planned, not shipped; they return `404`. Mapped features live in [features/README.md](features/README.md); drive only what is mapped there.

All commands run from the repository root. `scripts/verify-sporesync.sh` is relative to this skill directory: `.agents/skills/verify-sporesync/scripts/verify-sporesync.sh`.

## Launch

Prepare the checkout once (resolves Maven dependencies into the ignored `.dev/` directories):

```bash
elide install && elide build
```

Start an isolated instance:

```bash
.agents/skills/verify-sporesync/scripts/verify-sporesync.sh start
```

- Runs `elide run` in `backend/` with `SERVER_PORT=$SPORESYNC_VERIFY_PORT` (default `18080`, never the app default `8080`) and `SERVER_ADDRESS=$SPORESYNC_VERIFY_BIND` (default `127.0.0.1`). The bind must be a numeric IPv4 or IPv6 address. `start` rejects names such as `localhost` before launching.
- T3 Preview runs on its own client and reaches this host by its network name, not by loopback. For a run that uses Preview, start with `SPORESYNC_VERIFY_BIND=0.0.0.0`. This is the app's own default bind, so it exposes the health endpoint on the host's networks for the length of the run.
- Refuses to start if the port is already listening. Use another port for a second instance: `SPORESYNC_VERIFY_PORT=18081 .agents/skills/verify-sporesync/scripts/verify-sporesync.sh start`.
- Ready means `GET http://127.0.0.1:$PORT/actuator/health` returns `"status":"UP"`. During startup the port first refuses connections, then health can briefly answer `503`. The script waits up to 120 s.
- The last output line is `RUN_DIR=/tmp/verify-sporesync/<timestamp>-<port>.<random>`. Keep it; every later step takes it. Override the root with `SPORESYNC_VERIFY_ROOT`. The root is made owner-only (`700`). `start` refuses a root that is a symlink, is owned by another account, or is inside the checkout, where run files would change the tree fingerprint.
- Interrupting or failing `start` (Ctrl-C, TERM, timeout) stops everything it launched.
- `RUN_DIR/instance.env` is the process handle. `APP` is the JVM that owns the port, and `CHAIN` is `elide run` → `elide java` → JVM. Each entry is `PID@start-time`, so a reused PID never matches. `STARTED_HEAD`, `STARTED_STATE` (`clean` or `dirty`), and `STARTED_TREE` (sha256 of HEAD plus uncommitted and untracked changes) record what was compiled. The script parses this file; do not source it. Read the port with `PORT="$(sed -n 's/^PORT=//p' "$RUN_DIR/instance.env")"`.

Data isolation: the current backend writes no data. It has no settings file, H2 database, or download directory yet. A run changes only the ignored `.dev/` build output and `RUN_DIR`. Two instances in the same checkout share `.dev/`, so start them one after another, not at the same time.

## Doctor

Read-only. Run it first, and again whenever anything looks off:

```bash
.agents/skills/verify-sporesync/scripts/verify-sporesync.sh doctor "$RUN_DIR"
```

It exits `0` only if every recorded process (same PID and start time) is alive with cwd `<repo>/backend`, the port's listener is the `APP` JVM, the working-tree fingerprint still equals `STARTED_TREE`, and health returns `200` with `"status":"UP"`. Any commit or uncommitted edit since launch fails doctor. Stop and relaunch, because `elide run` compiles only at startup. Never drive an instance that doctor does not pass, or one this run did not start.

## Drive

The user surface today is an operator's HTTP client or browser hitting the actuator. Use the real port and the real path, not test slices or `@SpringBootTest`.

Define `PORT` and `probe` per [Driving conventions](features/README.md#driving-conventions), then probe each mapped sub-feature:

```bash
probe health /actuator/health
```

Browser view in T3 (requires `SPORESYNC_VERIFY_BIND=0.0.0.0`): call `preview_status`. If no automation-capable tab is attached, call `preview_open`. Then call `preview_navigate` with `target` `{"kind":"environment-port","port":$PORT,"path":"/actuator/health"}`, followed by `preview_snapshot` with `save=true`. Copy the returned `screenshotPath` to `$RUN_DIR/evidence/health.png`. A `url` of `http://127.0.0.1:$PORT` points at the Preview client's own loopback, not this host's, so it fails.

If `preview_snapshot` fails, call `preview_status` and check the tab URL. Confirm the response with `preview_evaluate`. If the URL is wrong, navigate again with the `environment-port` target, wait for text `UP`, and retry the snapshot. If capture still fails, open a fresh Preview tab and repeat once. Save the evaluated URL and body to `$RUN_DIR/evidence/health.preview-dom.json`, report the Preview error, and mark screenshot capture blocked. Do not switch to another browser tool.

Per-feature recipes: [features/README.md](features/README.md).

## Evidence

Everything for a run goes under `RUN_DIR`. Cleanup deletes only `instance.env`.

| Path | Contents |
| --- | --- |
| `RUN_DIR/app.log` | `elide run` build lines, then ECS JSON logs, one object per line. The `Started SporeSyncApplicationKt` message and `process.pid` identify the instance. |
| `RUN_DIR/evidence/doctor.txt` | `doctor` output: `(set -o pipefail; .agents/skills/verify-sporesync/scripts/verify-sporesync.sh doctor "$RUN_DIR" \| tee "$RUN_DIR/evidence/doctor.txt")`. Without `pipefail`, `tee` hides a failing doctor. |
| `RUN_DIR/evidence/<feature-id>.{status,headers.txt,json}` | Status code, headers, and body from each `probe`, plus `health.png` from Preview |

Proof standards:

- Hit the instance doctor verified, on its own port. A response from `8080` or from another checkout proves nothing about this run.
- Record the request (method, URL), status code, headers, and body, not just a screenshot.
- Report the `HEAD` SHA and the `clean`/`dirty` state from `doctor.txt` with the proof. A `dirty` run proves the uncommitted tree identified by its tree sha256, not the bare HEAD.
- Do not claim a `docs/SPEC.md` feature works because the spec lists it. It must be in `features/` and observed.

## Cleanup

```bash
.agents/skills/verify-sporesync/scripts/verify-sporesync.sh stop "$RUN_DIR"
```

This sends `TERM` to the recorded `APP` and `CHAIN` processes, but only to those whose PID, start time, and cwd (this checkout's `backend/`) all still match. It waits 20 s, sends `KILL` to exact survivors, waits up to 5 s for them to exit (a zombie counts as exited), confirms the port is free, then deletes `instance.env`. Never kill by process name (`pkill java`, `pkill elide`): other Elide or JVM processes may belong to the user. Do not signal only `elide run`, because that orphans the JVM, which keeps the port. Run `stop` after failed attempts too. `start` already stops its own chain if it fails or is interrupted. `app.log` and `evidence/` survive cleanup; confirm with `ls "$RUN_DIR/evidence"`.

## Helpers

`scripts/verify-sporesync.sh` (executable, bash 3.2+ on macOS or Linux). It checks for `curl`, `git`, `lsof`, `mktemp`, `pgrep`, `ps`, and `shasum` before doing anything, and `start` also requires `elide`.

| Command | Effect |
| --- | --- |
| `verify-sporesync.sh start` | Checks that `elide --version` matches `.elideversion`, launches, waits for UP, writes `instance.env`, and prints `RUN_DIR=` |
| `verify-sporesync.sh doctor RUN_DIR` | Read-only health, ownership, and working-tree check; non-zero on any failure |
| `verify-sporesync.sh stop RUN_DIR` | Stops exactly the recorded PIDs and keeps evidence |

Environment: `SPORESYNC_VERIFY_PORT` (default `18080`), `SPORESYNC_VERIFY_BIND` (default `127.0.0.1`), `SPORESYNC_VERIFY_ROOT` (default `/tmp/verify-sporesync`). Checks always use `127.0.0.1`, whatever the bind address.
