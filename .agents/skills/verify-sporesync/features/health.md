# Health check

An operator or orchestrator asks SporeSync whether it is up. `GET /actuator/health` answers `200` with `{"groups":["liveness","readiness"],"status":"UP"}`, and the two groups can be probed on their own. Health is the only exposed actuator endpoint, and every other route answers `404`.

## Sub-features

- `health` reports aggregate status and lists the `liveness` and `readiness` groups.
- `health-liveness` reports the liveness group alone.
- `health-readiness` reports the readiness group alone.
- `actuator-index` lists links to the exposed endpoints, which today are only the health endpoints.
- `not-found` answers `404` for unexposed actuator endpoints and for routes that have not shipped.

## How to get to it (user POV)

- Run `curl http://127.0.0.1:$PORT/actuator/health` in a terminal. This is the README check, on the verification port.
- Open `http://127.0.0.1:$PORT/actuator/health` in a browser.
- Probe `/actuator/health/liveness` or `/actuator/health/readiness`, as a container orchestrator would.

## Driving it with verify-sporesync

Preconditions:

- `(set -o pipefail; .agents/skills/verify-sporesync/scripts/verify-sporesync.sh doctor "$RUN_DIR" | tee "$RUN_DIR/evidence/doctor.txt")` exits `0`.
- `PORT` is set and `probe` is defined, per [Driving conventions](./README.md#driving-conventions).

- **Aggregate health.** Run `probe health /actuator/health`. It prints `200`. `health.headers.txt` has `Content-Type: application/vnd.spring-boot.actuator.v3+json`, and `health.json` is `{"groups":["liveness","readiness"],"status":"UP"}`.
- **Liveness.** Run `probe health-liveness /actuator/health/liveness`. It prints `200`, and `health-liveness.json` is `{"status":"UP"}`.
- **Readiness.** Run `probe health-readiness /actuator/health/readiness`. It prints `200`, and `health-readiness.json` is `{"status":"UP"}`.
- **Actuator index.** Run `probe actuator-index /actuator`. It prints `200`, and the `_links` keys in `actuator-index.json` are exactly `self`, `health`, and `health-path`.
- **Not found.** Run `probe not-found-actuator-info /actuator/info`, `probe not-found-root /`, and `probe not-found-api-status /api/status`. Each prints `404`, with a JSON body whose `status` is `404` and whose `path` is the requested path.
- **Browser view.** This requires an instance started with `SPORESYNC_VERIFY_BIND=0.0.0.0`. In T3, call `preview_status`, call `preview_open` if no automation-capable tab is attached, call `preview_navigate` with `target` `{"kind":"environment-port","port":$PORT,"path":"/actuator/health"}`, then call `preview_snapshot` with `save=true`. Copy `screenshotPath` to `$RUN_DIR/evidence/health.png`. The page shows the JSON body with `"status":"UP"`.

## Gotchas

- During startup the port first refuses connections, and health can briefly answer `503` before `UP`. `start` waits through both. Do not treat either as a failure.
- A `200` on port `8080` may come from a different SporeSync, such as the user's own `elide run`. Only `$PORT` from `instance.env` counts.
- The instance binds `127.0.0.1` by default. In that mode, Preview navigation fails because Preview reaches the host by network name. Relaunch with `SPORESYNC_VERIFY_BIND=0.0.0.0` for the browser view.
- `/`, `/api/*`, and the planned UI routes return `404` today. That is expected, not a regression.
