---
name: verify-sporesync
description: Launch, health-check, drive, capture evidence from, and clean up a real SporeSync backend (Kotlin/Spring Boot on Elide) or frontend dev server (React on Vite+) over HTTP and T3 Preview. Use when proving SporeSync behavior in the running app rather than in unit tests.
---

# Verify SporeSync

SporeSync today has two parts that launch separately. The backend is a Spring Boot app run by Elide 1.6.0. Its only user-facing route is the operator health check: `GET /actuator/health`, its `liveness` and `readiness` groups, and the `/actuator` index. The frontend is a React app served by the Vite+ dev server (`vp dev`, on Node.js). Its only route is `/`, a page with a "SporeSync" heading. The backend does not serve the frontend, and the backend has no `/api` routes. Every other feature in `docs/SPEC.md` is planned, not shipped. Mapped features live in [features/README.md](features/README.md); drive only what is mapped there.

All commands run from the repository root. `scripts/verify-sporesync.sh` is relative to this skill directory: `.agents/skills/verify-sporesync/scripts/verify-sporesync.sh`.

## Launch

Prepare the checkout once. The commands resolve Maven dependencies into the ignored `.dev/` directories and npm packages into the ignored `frontend/node_modules/`:

```bash
elide install && elide build
vp -C frontend install
```

Start an isolated instance of one component. `start` with no component starts the backend:

```bash
.agents/skills/verify-sporesync/scripts/verify-sporesync.sh start
.agents/skills/verify-sporesync/scripts/verify-sporesync.sh start frontend
```

- The backend runs `elide run` in `backend/` with `SERVER_PORT=$SPORESYNC_VERIFY_PORT` (default `18080`, never the app default `8080`) and `SERVER_ADDRESS=$SPORESYNC_VERIFY_BIND` (default `127.0.0.1`).
- The frontend runs `vp dev --host $SPORESYNC_VERIFY_BIND --port $SPORESYNC_VERIFY_PORT --strictPort` in `frontend/` (default port `15173`, never the Vite default `5173`).
- The bind must be `127.0.0.1` or `0.0.0.0`, because readiness and doctor probe `127.0.0.1`. `start` rejects any other value, and any port outside `1`-`65535`, before launching.
- The app gets a minimal environment: `HOME`, `PATH`, `USER`, `LOGNAME`, `TMPDIR`, `LANG`, `LC_ALL`, and `LC_CTYPE` when set. The backend also gets `SERVER_PORT` and `SERVER_ADDRESS`. Ambient settings such as `MANAGEMENT_*`, `SPRING_APPLICATION_JSON`, the README's `LOGGING_LEVEL_COM_SPORESYNC` debug toggle, `VITE_*`, or `NODE_OPTIONS` never reach it, so the run reflects the checkout's own configuration.
- T3 Preview's `environment-port` target opens `http://localhost:$PORT` in a headless tab on this host, so the default `127.0.0.1` bind is enough. `SPORESYNC_VERIFY_BIND=0.0.0.0` exposes the instance on the host's networks for the length of the run. Use it only for a browser on another machine.
- Refuses to start if the port is already listening. Use another port for a second instance: `SPORESYNC_VERIFY_PORT=18081 .agents/skills/verify-sporesync/scripts/verify-sporesync.sh start`.
- Backend ready means `GET http://127.0.0.1:$PORT/actuator/health` returns `"status":"UP"`. During startup the port first refuses connections, then health can briefly answer `503`. Frontend ready means `GET http://127.0.0.1:$PORT/` returns `200` with `<div id="root"></div>`. The script waits up to 120 s for either.
- The last output line is `RUN_DIR=<root>/<timestamp>-<port>.<random>`, with the root resolved to its physical path. On macOS the default prints as `/private/tmp/verify-sporesync/...`. Keep it; every later step takes it. Override the root with `SPORESYNC_VERIFY_ROOT`. The root is made owner-only (`700`). `start` refuses a root that is a symlink, is owned by another account, or is inside the checkout, where run files would change the tree fingerprint.
- Interrupting or failing `start` (Ctrl-C, TERM, timeout) stops everything it launched.
- `RUN_DIR/instance.env` is the process handle. `COMPONENT` is `backend` or `frontend`. `APP` is the process that owns the port. `CHAIN` is the launcher and its descendants: `elide run` → `elide java` → JVM for the backend, or `vp` → `node` → `node` for the frontend. Each entry is `PID@start-time`, so a reused PID never matches. `STARTED_HEAD`, `STARTED_STATE` (`clean` or `dirty`), and `STARTED_TREE` (sha256 of HEAD plus uncommitted and untracked changes) record the tree the instance started from. The script parses this file; do not source it. Read the port with `PORT="$(sed -n 's/^PORT=//p' "$RUN_DIR/instance.env")"`.

Data isolation: the current backend writes no data. It has no settings file, H2 database, or download directory yet. A run changes only `RUN_DIR` and ignored build output: `.dev/` for the backend, and Vite's cache in `frontend/node_modules/` for the frontend. Instances of one component share that output, so start them one after another, not at the same time.

## Doctor

Read-only. Run it first, and again whenever anything looks off:

```bash
.agents/skills/verify-sporesync/scripts/verify-sporesync.sh doctor "$RUN_DIR"
```

It exits `0` only if all of these hold:

- Every recorded process (same PID and start time) is alive, with cwd `<repo>/backend` or `<repo>/frontend` to match `COMPONENT`.
- The port's listener is `APP`.
- The working-tree fingerprint still equals `STARTED_TREE`.
- The readiness URL answers `200` with its marker: `"status":"UP"` from the backend's `/actuator/health`, or `<div id="root"></div>` from the frontend's `/`.

Any commit or uncommitted edit since launch fails doctor. Stop and relaunch, because `elide run` compiles only at startup. The frontend fails the same way, even though Vite reloads edits, so `doctor.txt` names the tree that was served. Never drive an instance that doctor does not pass, or one this run did not start.

## Drive

Backend users are operators whose HTTP client or browser hits the actuator. Frontend users open `/` in a browser. Use the real port and the real path, not test slices, `@SpringBootTest`, or Vitest.

Define `PORT` and `probe` per [Driving conventions](features/README.md#driving-conventions), then probe each mapped sub-feature:

```bash
probe health /actuator/health
```

Browser view in T3: call `preview_status`. If no automation-capable tab is attached, call `preview_open` with `open=false`. Then call `preview_navigate` with `target` `{"kind":"environment-port","port":$PORT,"path":"<path>"}`, followed by `preview_snapshot` with `save=true`. Copy the returned `screenshotPath` to `$RUN_DIR/evidence/<feature-id>.png`. Close a tab you opened with `t3_preview_close` after the last snapshot.

If `preview_snapshot` fails, call `preview_status` and check the tab URL. Confirm the page with `preview_evaluate`. If the URL is wrong, navigate again with the `environment-port` target, wait for the feature's expected text, and retry the snapshot. If capture still fails, open a fresh Preview tab and repeat once. Save the evaluated URL and page content to `$RUN_DIR/evidence/<feature-id>.preview-dom.json`, report the Preview error, and mark screenshot capture blocked. Do not switch to another browser tool.

Per-feature recipes: [features/README.md](features/README.md).

## Evidence

Everything for a run goes under `RUN_DIR`. Cleanup deletes only `instance.env`.

| Path | Contents |
| --- | --- |
| `RUN_DIR/app.log` | The launcher's output. For the backend, `elide run` build lines, then ECS JSON logs, one object per line, where the `Started SporeSyncApplicationKt` message and `process.pid` identify the instance. For the frontend, the Vite+ banner and its `Local:` URL. |
| `RUN_DIR/evidence/doctor.txt` | `doctor` output: `(set -o pipefail; .agents/skills/verify-sporesync/scripts/verify-sporesync.sh doctor "$RUN_DIR" \| tee "$RUN_DIR/evidence/doctor.txt")`. Without `pipefail`, `tee` hides a failing doctor. |
| `RUN_DIR/evidence/<feature-id>.{request.txt,status,headers.txt,body}` | Request method and URL, status code, headers, and body from each `probe` |
| `RUN_DIR/evidence/<feature-id>.{png,preview-dom.json}` | Preview screenshot and `preview_evaluate` result |

Proof standards:

- Hit the instance doctor verified, on its own port. A response from `8080`, `5173`, or another checkout proves nothing about this run.
- Record the request (method, URL), status code, headers, and body, not just a screenshot.
- Report the `HEAD` SHA and the `clean`/`dirty` state from `doctor.txt` with the proof. A `dirty` run proves the uncommitted tree identified by its tree sha256, not the bare HEAD.
- Do not claim a `docs/SPEC.md` feature works because the spec lists it. It must be in `features/` and observed.

## Cleanup

```bash
.agents/skills/verify-sporesync/scripts/verify-sporesync.sh stop "$RUN_DIR"
```

This sends `TERM` to the recorded `APP` and `CHAIN` processes, but only to those whose PID, start time, and cwd (the component's directory in this checkout) all still match. It waits 20 s, sends `KILL` to exact survivors, waits up to 5 s for them to exit (a zombie counts as exited), confirms the port is free, then deletes `instance.env`. Never kill by process name (`pkill java`, `pkill elide`, `pkill node`), because other processes with those names may belong to the user. Do not signal only the launcher. Signalling only `elide run` or `vp` orphans the server, which keeps the port. Run `stop` after failed attempts too. `start` already stops its own chain if it fails or is interrupted. `app.log` and `evidence/` survive cleanup; confirm with `ls "$RUN_DIR/evidence"`.

## Helpers

`scripts/verify-sporesync.sh` (executable, bash 3.2+ on macOS or Linux). It checks for `curl`, `git`, `lsof`, `mktemp`, `pgrep`, `ps`, and `shasum` before doing anything. `start` also requires `elide` for the backend or `vp` for the frontend.

| Command | Effect |
| --- | --- |
| `verify-sporesync.sh start [backend\|frontend]` | Launches the component (default `backend`), waits for ready, writes `instance.env`, and prints `RUN_DIR=`. For the backend it first checks that `elide --version` matches `.elideversion`. |
| `verify-sporesync.sh doctor RUN_DIR` | Read-only readiness, ownership, and working-tree check; non-zero on any failure |
| `verify-sporesync.sh stop RUN_DIR` | Stops exactly the recorded PIDs and keeps evidence |

Environment: `SPORESYNC_VERIFY_PORT` (default `18080` for the backend, `15173` for the frontend), `SPORESYNC_VERIFY_BIND` (default `127.0.0.1`), `SPORESYNC_VERIFY_ROOT` (default `/tmp/verify-sporesync`). Checks always use `127.0.0.1`, whatever the bind address.
