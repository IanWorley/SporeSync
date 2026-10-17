# SporeSync verification map

This directory is the maintained source for verifying SporeSync's user-facing behavior. Read the index before driving the app, then use the matching feature file as the recipe.

Today the only shipped user-facing feature is the health endpoint. The map expands as feature routes land. Add a file here when a route from `docs/SPEC.md` (the `/api` endpoints, the `/`, `/settings`, `/autoqueue`, and `/about` UI routes) actually ships, not before.

## Baseline preconditions

- Start an instance with `.agents/skills/verify-sporesync/scripts/verify-sporesync.sh start` and keep the printed `RUN_DIR`.
- `.agents/skills/verify-sporesync/scripts/verify-sporesync.sh doctor "$RUN_DIR"` exits `0`.
- Read this run's port with `PORT="$(sed -n 's/^PORT=//p' "$RUN_DIR/instance.env")"`. Do not source `instance.env`.
- Never drive an instance that this run did not start.

## Driving conventions

- Address the app from the shell as `http://127.0.0.1:$PORT`, never the default `8080`.
- Use `curl` for status, headers, and body. Use T3 Preview (`preview_navigate` with an `environment-port` target, `preview_snapshot` with `save=true`) for the browser view. Preview needs an instance started with `SPORESYNC_VERIFY_BIND=0.0.0.0`.
- Treat every command as literal.
- Write artifacts to `$RUN_DIR/evidence/` and name them after the feature ID.

## Proof and skip reporting

- HTTP proof includes the request URL, status code, response headers, and body.
- Browser proof is a saved Preview screenshot that shows the URL's response.
- Report the `HEAD` SHA and `clean`/`dirty` state from doctor output with every proof.
- Report an unreachable path with the attempted command and the unmet precondition. Do not report it as verified through a different path.

## Feature entry contract

Each feature file starts with an H1 title and one paragraph describing the user-visible behavior. It then uses exactly four H2 sections in this order: `Sub-features`, `How to get to it (user POV)`, `Driving it with verify-sporesync`, and `Gotchas`. The driving section starts with `Preconditions:` and pairs each action with an exact command and an observable result.

## Features

- [Health check](./health.md) covers the aggregate health response, the liveness and readiness groups, and the actuator index.
