# Agent instructions

SporeSync is being rebuilt. Read `docs/plan.md` for scope and unresolved choices.
The implementation includes a Python scanner, Kotlin/Spring Boot SSH inventory,
and a minimal Vite/React/TypeScript frontend. Dashboard and transfer features
remain pending. Elide is the
backend build tool; do not substitute another build tool. See
`docs/backend-build.md` for verified compatibility and limitations.
Prefer simple, typed interfaces and named constants for policies.
SSH connection settings live in `sporesync_settings`; credentials, known-hosts,
and the local scanner path remain external. See README for setup and migration.

## Required checks

- CI: `.github/workflows/ci.yml` runs backend build/format/tests, frontend
  type-check/build, and scanner unit/SSH tests on every PR (including stacked
  PRs), pushes to `main`, and manual dispatch. Keep these checks aligned with
  the local commands below. The Elide Linux archive is release- and checksum-pinned.

- Backend changes: from `backend/`, run `elide build` and `elide test`.
  Tests require Docker and use disposable PostgreSQL and SSH Testcontainers; missing
  Docker must fail rather than silently skip integration verification.
- Kotlin formatting: from `backend/`, run `elide format -- -n src`.
- Backend commands must run from `backend/` so Spring finds `config/`.
- Frontend changes: from `frontend/`, run `npm ci` and `npm run build`.
  Verify `/api/status` through Vite against a running backend when changing the proxy.
  Spring serves `frontend/dist/` externally; build it before production-mode verification.
- Scanner changes: `python3 -m unittest discover -s tests -v`.
- SSH or fixture changes: also run
  `SPORESYNC_SSH_TEST=1 python3 -m unittest discover -s tests -v` (requires Docker).
- Report commands and results; never present skipped integration tests as passed.
- Update this file and README when setup or testing commands change.

Aim for 200–400 changed lines per implementation PR; keep each under 500.
The initial repository reset is the authorized exception. Use conventional
commits. PR descriptions must identify the agent, model, provider, and reasoning
setting when available. Do not commit credentials or generated artifacts.
