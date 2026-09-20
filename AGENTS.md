# Agent instructions

SporeSync is being rebuilt. Read `docs/plan.md` for scope and unresolved choices.
The current implementation is a Python scanner, with no Java backend or UI yet.
Verify Elide compatibility before scaffolding Spring; do not substitute another
build tool. Prefer simple, typed interfaces and named constants for policies.

## Required checks

- Scanner changes: `python3 -m unittest discover -s tests -v`.
- SSH or fixture changes: also run
  `SPORESYNC_SSH_TEST=1 python3 -m unittest discover -s tests -v` (requires Docker).
- Report commands and results; never present skipped integration tests as passed.
- Update this file and README when setup or testing commands change.

Aim for 200–400 changed lines per implementation PR; keep each under 500.
The initial repository reset is the authorized exception. Use conventional
commits. PR descriptions must identify the agent, model, provider, and reasoning
setting when available. Do not commit credentials or generated artifacts.
