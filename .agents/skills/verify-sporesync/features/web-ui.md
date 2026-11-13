# Web UI

A user opens SporeSync on the backend's port and sees the same "SporeSync" heading the dev server shows. The backend serves the production bundle from `vp build`: `/` returns its `index.html`, hashed files under `/assets/` return as-is, and any other path without a file extension returns `index.html` too, so a reload on a client route still loads the app. Paths under `/api` and `/actuator`, and missing files, are never rewritten to `index.html`.

## Sub-features

- `web-shell` is the built `index.html` at `/`: the title `SporeSync`, an empty `<div id="root"></div>`, and a module script under `/assets/`.
- `web-asset` is the hashed JavaScript bundle that script names.
- `web-fallback` is the same `index.html` returned for a client route such as `/settings`.
- `not-found-asset` is the JSON `404` for a missing file such as `/missing.js`.
- `web-home` is the page a browser renders from the shell: one `h1` whose text is `SporeSync`.

## How to get to it (user POV)

- Open `http://127.0.0.1:$PORT/` in a browser, the port the backend listens on. The Docker image and `elide run` with a built `frontend/dist/` serve it the same way on `8080`.

## Driving it with verify-sporesync

Preconditions:

- `.agents/skills/verify-sporesync/scripts/verify-sporesync.sh start` printed `RUN_DIR`, and `(set -o pipefail; .agents/skills/verify-sporesync/scripts/verify-sporesync.sh doctor "$RUN_DIR" | tee "$RUN_DIR/evidence/doctor.txt")` exits `0`.
- `PORT` is set and `probe` is defined, per [Driving conventions](./README.md#driving-conventions).

- **Shell.** Run `probe web-shell /`. It prints `200`, and `web-shell.headers.txt` has `Content-Type: text/html`. `cmp "$RUN_DIR/evidence/web-shell.body" "$RUN_DIR/frontend/index.html"` exits `0`, so the backend served this run's bundle.
- **Asset.** Run `probe web-asset "$(grep -oE '/assets/index-[A-Za-z0-9_-]+\.js' "$RUN_DIR/evidence/web-shell.body")"`. It prints `200`, and `web-asset.headers.txt` has `Content-Type: text/javascript`.
- **Fallback.** Run `probe web-fallback /settings`. It prints `200`, and `cmp "$RUN_DIR/evidence/web-shell.body" "$RUN_DIR/evidence/web-fallback.body"` exits `0`.
- **Missing file.** Run `probe not-found-asset /missing.js`. It prints `404`, with a JSON body whose `path` is `/missing.js`.
- **Rendered heading.** In T3, call `preview_status`, and call `preview_open` with `open=false` if no automation-capable tab is attached. Call `preview_navigate` with `target` `{"kind":"environment-port","port":$PORT,"path":"/"}`. Call `preview_wait_for` with `selector` `h1` and `text` `SporeSync`. Call `preview_evaluate` with `({ url: location.href, h1: [...document.querySelectorAll("h1")].map((h) => h.textContent) })`. Its `value.h1` is `["SporeSync"]`. Save that value to `$RUN_DIR/evidence/web-home.preview-dom.json`. Call `preview_snapshot` with `save=true` and copy `screenshotPath` to `$RUN_DIR/evidence/web-home.png`.

## Gotchas

- `/settings` has no client route yet, so a browser there renders the router's not-found view, not a settings page. `web-fallback` proves only the server rewrite.
- `web-shell` comes from `index.html` alone. Only `web-home` proves the bundle runs.
- `/` answers `Content-Type: text/html;charset=UTF-8` and the fallback answers `text/html`. Both are the same file.
