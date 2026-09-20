# Agent instructions

SporeSync is being rebuilt. Read `docs/plan.md` for scope and unresolved choices.
The implementation includes a Python scanner and Kotlin/Spring Boot scaffold;
there is no dashboard or backend SSH/transfer functionality yet. Elide is the
backend build tool; do not substitute another build tool. See
`docs/backend-build.md` for verified compatibility and limitations.
Prefer simple, typed interfaces and named constants for policies.

## Required checks

- Backend changes: from `backend/`, run `elide build` and `elide test`.
  Tests require Docker and use a disposable PostgreSQL Testcontainer; missing
  Docker must fail rather than silently skip integration verification.
- Kotlin formatting: from `backend/`, run `elide format -- -n src`.
- Backend commands must run from `backend/` so Spring finds `config/`.
- Scanner changes: `python3 -m unittest discover -s tests -v`.
- SSH or fixture changes: also run
  `SPORESYNC_SSH_TEST=1 python3 -m unittest discover -s tests -v` (requires Docker).
- Report commands and results; never present skipped integration tests as passed.
- Update this file and README when setup or testing commands change.

Aim for 200–400 changed lines per implementation PR; keep each under 500.
The initial repository reset is the authorized exception. Use conventional
commits. PR descriptions must identify the agent, model, provider, and reasoning
setting when available. Do not commit credentials or generated artifacts.
