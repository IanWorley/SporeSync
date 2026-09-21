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

## Source layout

Backend Kotlin code uses `model/` for domain types, persistence, and business
logic, `controller/` for HTTP endpoints, and `config/` for runtime settings.
The React frontend supplies the view: `model/` contains typed contracts,
`view/` contains components and styles, `controller/` contains hooks and API
requests, and `config/` contains shared endpoint and polling constants.
Entry points and tool manifests stay at their standard locations; runtime
backend configuration stays in `backend/config/`. The remote scanner remains
one independently uploadable script.
Use interfaces for I/O-facing service contracts (scanning, file transfer, job
persistence, and settings storage); keep immutable data models concrete.


