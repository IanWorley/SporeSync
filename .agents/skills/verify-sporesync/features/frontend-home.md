# Frontend home page

A user opens SporeSync's web UI at `/` and sees the nav bar described in [Frontend pages and nav](./frontend-pages.md) and, below it, one heading, "SporeSync", on an otherwise empty page. The Vite+ dev server (`vp dev`) serves it from `frontend/`. The server answers `/` with an HTML shell whose `#root` element is empty, and React renders the heading in the browser. An HTTP client sees the page title `SporeSync` but never the heading.

## Sub-features

- `frontend-shell` is the HTML the dev server returns for `/`: the title `SporeSync`, an empty `<div id="root"></div>`, and the `/src/main.tsx` module script.
- `frontend-home` is the page a browser renders from that shell: one `h1` whose text is `SporeSync`.

## How to get to it (user POV)

- Run `vp dev` in `frontend/` and open the `Local:` URL it prints. The default is `http://localhost:5173/`. A verification run uses `$PORT` from `instance.env` instead.

## Driving it with verify-sporesync

Preconditions:

- `vp -C frontend install` has run.
- `.agents/skills/verify-sporesync/scripts/verify-sporesync.sh start frontend` printed `RUN_DIR`, and `(set -o pipefail; .agents/skills/verify-sporesync/scripts/verify-sporesync.sh doctor "$RUN_DIR" | tee "$RUN_DIR/evidence/doctor.txt")` exits `0`.
- `PORT` is set and `probe` is defined, per [Driving conventions](./README.md#driving-conventions).

- **HTML shell.** Run `probe frontend-shell /`. It prints `200`, and `frontend-shell.headers.txt` has `Content-Type: text/html`. Run `grep -cF -e '<title>SporeSync</title>' -e '<div id="root"></div>' -e '<script type="module" src="/src/main.tsx"></script>' "$RUN_DIR/evidence/frontend-shell.body"`. It prints `3`.
- **Rendered heading.** In T3, call `preview_status`, and call `preview_open` with `open=false` if no automation-capable tab is attached. Call `preview_navigate` with `target` `{"kind":"environment-port","port":$PORT,"path":"/"}`. Its `url` is `http://localhost:$PORT/` and its `title` is `SporeSync`. Call `preview_wait_for` with `selector` `h1` and `text` `SporeSync`. Call `preview_evaluate` with `({ url: location.href, h1: [...document.querySelectorAll("h1")].map((h) => h.textContent) })`. Its `value.h1` is `["SporeSync"]`. Save that value to `$RUN_DIR/evidence/frontend-home.preview-dom.json`. Call `preview_snapshot` with `save=true` and copy `screenshotPath` to `$RUN_DIR/evidence/frontend-home.png`. The screenshot shows the nav bar with `Files` in bold and "SporeSync" below it on a blank page.
- **No Preview.** If Preview is unreachable, report `frontend-home` as `could not run` with the Preview error. `frontend-shell` does not prove the heading.

## Gotchas

- The heading exists only after React runs, so `curl` and `probe` never show it. The shell comes from `index.html` alone, so its `200` says nothing about whether the modules under `src/` compile. Only the Preview check proves the heading.
- `vp dev` proxies `/api` to `http://localhost:8080`, which `frontend/vite.config.ts` hard-codes. It never reaches a verification backend on its own port. The home page calls no `/api` route today. A `/api` request on the frontend port goes to whatever listens on `8080`, such as the user's own backend.
- Vite answers `403` to a request whose `Host` header is a hostname other than `localhost`, such as `<name>.local`, even with `SPORESYNC_VERIFY_BIND=0.0.0.0`. Preview's `environment-port` target uses `localhost`, so Preview is unaffected. A browser on another machine must use the host's IP address.
- A `200` on port `5173` may come from the user's own `vp dev`. Only `$PORT` from `instance.env` counts.
