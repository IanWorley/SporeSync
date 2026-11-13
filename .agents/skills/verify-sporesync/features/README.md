# SporeSync verification map

This directory is the maintained source for verifying SporeSync's user-facing behavior. Read the index before driving the app, then use the matching feature file as the recipe.

Three user-facing features have shipped: the backend's health endpoint, the UI the backend serves, and the dev server's home page. Add a file here when another route from `docs/SPEC.md` (the `/api` endpoints, the `/api/stream` SSE endpoint, or the `/settings`, `/autoqueue`, and `/about` UI routes) actually ships, not before.

## Baseline preconditions

- Start the feature's component with `.agents/skills/verify-sporesync/scripts/verify-sporesync.sh start` (backend) or `.agents/skills/verify-sporesync/scripts/verify-sporesync.sh start frontend`, and keep the printed `RUN_DIR`.
- `.agents/skills/verify-sporesync/scripts/verify-sporesync.sh doctor "$RUN_DIR"` exits `0`.
- Never drive an instance that this run did not start.

## Driving conventions

- Address the app from the shell as `http://127.0.0.1:$PORT`, never the app defaults `8080` or `5173`.
- Drive every HTTP sub-feature with `probe <feature-id> <path>`. It writes `<feature-id>.request.txt` (method and full URL), `<feature-id>.headers.txt`, `<feature-id>.body`, and `<feature-id>.status` to `$RUN_DIR/evidence/`, prints the status, and exits non-zero only if curl fails. It bypasses any `http_proxy` or `ALL_PROXY` setting, so the request reaches this instance. Define it once per shell:

```bash
PORT="$(sed -n 's/^PORT=//p' "$RUN_DIR/instance.env")"
probe() {
  printf 'GET %s\n' "http://127.0.0.1:$PORT$2" >"$RUN_DIR/evidence/$1.request.txt"
  (set -o pipefail; curl --noproxy '*' -sS -D "$RUN_DIR/evidence/$1.headers.txt" -o "$RUN_DIR/evidence/$1.body" \
    -w '%{http_code}\n' "http://127.0.0.1:$PORT$2" | tee "$RUN_DIR/evidence/$1.status")
}
```

- Do not source `instance.env`. The helper parses it.
- Use T3 Preview (`preview_navigate` with an `environment-port` target, `preview_snapshot` with `save=true`) for the browser view.
- Treat every command as literal.
- Name every artifact after the feature ID it proves.

## Proof and skip reporting

- HTTP proof includes the request URL, status code, response headers, and body.
- Browser proof is a saved Preview screenshot of the page, plus the `preview_evaluate` value for any text the claim rests on.
- Report the `HEAD` SHA and `clean`/`dirty` state from doctor output with every proof.
- Report an unreachable path with the attempted command and the unmet precondition. Do not report it as verified through a different path.

## Feature entry contract

Each feature file starts with an H1 title and one paragraph describing the user-visible behavior. It then uses exactly four H2 sections in this order: `Sub-features`, `How to get to it (user POV)`, `Driving it with verify-sporesync`, and `Gotchas`. The driving section starts with `Preconditions:` and pairs each action with an exact command and an observable result.

## Features

- [Health check](./health.md) covers the backend's aggregate health response, the liveness and readiness groups, the actuator index, and `404` routes.
- [Web UI](./web-ui.md) covers the built frontend bundle the backend serves at `/`, its assets, the client-route fallback, and the heading a browser renders.
- [Frontend home page](./frontend-home.md) covers the dev server's HTML shell at `/` and the "SporeSync" heading a browser renders from it.
