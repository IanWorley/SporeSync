# SporeSync

SporeSync is being rebuilt around the [starting brief](docs/plan.md).
The implementation includes a dependency-free Python remote scanner and a
Kotlin/Spring Boot backend with SSH inventory discovery and a minimal
Vite/React/TypeScript frontend. Downloads and dashboard features are subsequent
slices in the plan.

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


