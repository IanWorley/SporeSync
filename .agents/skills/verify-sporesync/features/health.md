# Health check

An operator or orchestrator asks SporeSync whether it is up. `GET /actuator/health` answers `200` with `{"groups":["liveness","readiness"],"status":"UP"}`, and the two groups can be probed on their own. Health is the only exposed actuator endpoint.

## Sub-features

- `health` reports aggregate status and lists the `liveness` and `readiness` groups.
- `health-liveness` reports the liveness group alone.
- `health-readiness` reports the readiness group alone.
- `actuator-index` lists links to the exposed endpoints, which today are only the health endpoints.

## How to get to it (user POV)

- Run `curl http://127.0.0.1:$PORT/actuator/health` in a terminal. This is the README check, on the verification port.
- Open `http://127.0.0.1:$PORT/actuator/health` in a browser.
- Probe `/actuator/health/liveness` or `/actuator/health/readiness`, as a container orchestrator would.

## Driving it with verify-sporesync

Preconditions:

- `verify-sporesync.sh doctor "$RUN_DIR"` exits `0`.
- `PORT="$(sed -n 's/^PORT=//p' "$RUN_DIR/instance.env")"` has set `$PORT`.

- **Aggregate health.** Request the endpoint. Run `(set -o pipefail; curl -sS -D "$RUN_DIR/evidence/health.headers.txt" -o "$RUN_DIR/evidence/health.json" -w '%{http_code}\n' "http://127.0.0.1:$PORT/actuator/health" | tee "$RUN_DIR/evidence/health.status")`. Status is `200`, `Content-Type` is `application/vnd.spring-boot.actuator.v3+json`, and the body is `{"groups":["liveness","readiness"],"status":"UP"}`.
- **Liveness.** Run `curl -sS -w '\n%{http_code}\n' "http://127.0.0.1:$PORT/actuator/health/liveness"`. The body is `{"status":"UP"}` and the status is `200`.
- **Readiness.** Run `curl -sS -w '\n%{http_code}\n' "http://127.0.0.1:$PORT/actuator/health/readiness"`. The body is `{"status":"UP"}` and the status is `200`.
- **Actuator index.** Run `curl -sS "http://127.0.0.1:$PORT/actuator"`. `_links` contains `self`, `health`, and `health-path`, and nothing else.
- **Not exposed.** Run `curl -sS -o /dev/null -w '%{http_code}\n' "http://127.0.0.1:$PORT/actuator/info"`. The status is `404`.
- **Browser view.** This requires an instance started with `SPORESYNC_VERIFY_BIND=0.0.0.0`. In T3, call `preview_status`, call `preview_open` if no automation-capable tab is attached, call `preview_navigate` with `target` `{"kind":"environment-port","port":$PORT,"path":"/actuator/health"}`, then call `preview_snapshot` with `save=true`. Copy `screenshotPath` to `$RUN_DIR/evidence/health.png`. The page shows the JSON body with `"status":"UP"`.

## Gotchas

- During startup the endpoint returns `503` before it returns `UP`. `start` waits for that. Do not treat an early `503` as a failure.
- A `200` on port `8080` may come from a different SporeSync, such as the user's own `elide run`. Only `$PORT` from `instance.env` counts.
- The instance binds `127.0.0.1` by default. In that mode, Preview navigation fails because Preview reaches the host by network name. Relaunch with `SPORESYNC_VERIFY_BIND=0.0.0.0` for the browser view.
- `/`, `/api/*`, and the planned UI routes return `404` today. That is expected, not a regression.
