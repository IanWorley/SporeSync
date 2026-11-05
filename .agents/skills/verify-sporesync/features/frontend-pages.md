# Frontend pages and nav

A user moves between SporeSync's four pages with the nav bar at the top of every page. It links `Files` (`/`), `Settings` (`/settings`), `Autoqueue` (`/autoqueue`), and `About` (`/about`), in that order, and shows the current page's link in bold. `/settings`, `/autoqueue`, and `/about` are placeholders today: each shows one heading with the page's name and nothing else. Opening one of these URLs directly renders the same page as clicking its link.

## Sub-features

- `frontend-nav` is the nav bar: a `nav` landmark named `Main` with four links, `Files`, `Settings`, `Autoqueue`, and `About`, whose `href`s are `/`, `/settings`, `/autoqueue`, and `/about`. The current page's link has `aria-current="page"`.
- `frontend-nav-click` is a click on a nav link, which changes the URL and the heading in the same tab.
- `frontend-settings` is the page at `/settings`: one `h1` whose text is `Settings`.
- `frontend-autoqueue` is the page at `/autoqueue`: one `h1` whose text is `Autoqueue`.
- `frontend-about` is the page at `/about`: one `h1` whose text is `About`.

## How to get to it (user POV)

- Open the `Local:` URL that `vp dev` prints and click a link in the nav bar.
- Type a page's URL, such as `http://localhost:5173/settings`. A verification run uses `$PORT` from `instance.env` instead.

## Driving it with verify-sporesync

Preconditions:

- `vp -C frontend install` has run.
- `.agents/skills/verify-sporesync/scripts/verify-sporesync.sh start frontend` printed `RUN_DIR`, and `(set -o pipefail; .agents/skills/verify-sporesync/scripts/verify-sporesync.sh doctor "$RUN_DIR" | tee "$RUN_DIR/evidence/doctor.txt")` exits `0`.
- `PORT` is set and `probe` is defined, per [Driving conventions](./README.md#driving-conventions).
- In T3, `preview_status` shows an automation-capable tab, or `preview_open` with `open=false` attached one.

Each Preview check below reads the page with `preview_evaluate` and this expression, called `PAGE_STATE` here:

```js
({
  url: location.href,
  h1: [...document.querySelectorAll("h1")].map((h) => h.textContent),
  nav: [...document.querySelectorAll('nav[aria-label="Main"] a')].map((a) => [a.textContent, a.getAttribute("href")]),
  current: [...document.querySelectorAll('nav a[aria-current="page"]')].map((a) => a.textContent),
})
```

- **Shell on a page path.** Run `probe frontend-shell-settings /settings`. It prints `200`, and `grep -cF '<div id="root"></div>' "$RUN_DIR/evidence/frontend-shell-settings.body"` prints `1`. The dev server answers every page path with the same HTML shell.
- **Each page by URL.** For each `<id> <path> <title>` in `frontend-settings /settings Settings`, `frontend-autoqueue /autoqueue Autoqueue`, and `frontend-about /about About`, call `preview_navigate` with `target` `{"kind":"environment-port","port":$PORT,"path":"<path>"}`, then `preview_wait_for` with `selector` `h1` and `text` `<title>`. `PAGE_STATE` has `url` `http://localhost:$PORT<path>`, `h1` `["<title>"]`, and `current` `["<title>"]`. Save it to `$RUN_DIR/evidence/<id>.preview-dom.json`. Call `preview_snapshot` with `save=true` and copy `screenshotPath` to `$RUN_DIR/evidence/<id>.png`. The screenshot shows the nav bar with the page's link in bold and the title below it.
- **Nav bar.** On any page, `PAGE_STATE.nav` is `[["Files","/"],["Settings","/settings"],["Autoqueue","/autoqueue"],["About","/about"]]`. Save it to `$RUN_DIR/evidence/frontend-nav.preview-dom.json`.
- **Click a link.** Navigate to path `/` and wait for `h1` `SporeSync`. Call `preview_act` with `action` `click` and `selector` `nav a[href="/settings"]`, then `preview_wait_for` with `selector` `h1`, `text` `Settings`, and `urlIncludes` `/settings`. `PAGE_STATE` has `url` `http://localhost:$PORT/settings`, `h1` `["Settings"]`, and `current` `["Settings"]`. Save it to `$RUN_DIR/evidence/frontend-nav-click.preview-dom.json`, and save a snapshot as `$RUN_DIR/evidence/frontend-nav-click.png`.
- **No Preview.** If Preview is unreachable, report each sub-feature as `could not run` with the Preview error. The shell probe does not prove any page.

## Gotchas

- The nav and the headings exist only after React runs, so `curl` and `probe` never show them. Every page path returns the same shell, so a `200` there does not prove the route exists. An unknown path such as `/nope` also returns `200` with the shell.
- `Files` is bold only on `/`. Each link matches its path exactly, so `/settings` never also marks `Files` as current.
- The pages call no `/api` route. See [Frontend home page](./frontend-home.md) for the dev server's `/api` proxy and its `Host` header gotchas, which apply here too.
