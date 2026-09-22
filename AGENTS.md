# Agent instructions

SporeSync is being rebuilt. Read `docs/plan.md` for scope and agreed behavior.
The implementation includes a Python scanner, Kotlin/Spring Boot SSH inventory,
durable SFTP downloads, scheduled discovery, and a Vite/React/TypeScript dashboard.
Gradle is the backend build tool. The included Elide Gradle plugin downloads the
managed Elide runtime. See `docs/backend-build.md` for toolchain and bootstrap
instructions.
Prefer simple, typed interfaces and named constants for policies.
SSH connection settings live in `sporesync_settings`; credentials, known-hosts,
and the local scanner path remain external. Choose KEY (default) or PASSWORD via
`SPORESYNC_SSH_AUTHENTICATION`; both scanning and SFTP use the selected method.
See README for setup and migration.

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



## Required checks

- CI: `.github/workflows/ci.yml` runs the Gradle backend build, SpotBugs,
  formatting, and tests; frontend type-check/build; and scanner unit/SSH tests
  on every PR (including stacked PRs), pushes to `main`, and manual dispatch.
  Keep these checks aligned with the local commands below. The bootstrap script
  downloads the Elide Gradle plugin source at
  `a1bb1307203acb44fa0d622aad4870c69f1144e1` with a pinned SHA-256.

- Backend changes: from `backend/`, set `JAVA_HOME` to Temurin Java 25, run
  `bash scripts/prepare-elide-plugin.sh`, then run `./gradlew build`. The included
  Elide plugin compiles with Java 17. The backend compiles with Java 25. For a
  Java 17 installation outside Gradle's usual locations, pass
  `-Porg.gradle.java.installations.paths="$JDK17,$JAVA_HOME"` to Gradle.
  `spotbugsTest` is disabled because SpotBugs analyzes production classes only.
  Tests require Docker and use disposable PostgreSQL and SSH Testcontainers; missing
  Docker must fail rather than silently skip integration verification.
- Kotlin formatting: from `backend/`, run `./gradlew elideCheckFormat` to check
  formatting and `./gradlew elideFormat` to apply it.
- Backend commands must run from `backend/` so Spring finds `config/`.
- SpotBugs compares medium and high findings with `config/spotbugs-baseline.xml`.
  The baseline records 32 reviewed findings. Review each baseline update before
  committing it. SpotBugs writes `build/reports/spotbugs/main.xml` and
  `build/reports/spotbugs/main.html`. Gradle writes test reports to
  `build/reports/tests/test`.
- Frontend changes: from `frontend/`, run `npm ci` and `npm run build`.
  Verify `/api/status` through Vite against a running backend when changing the proxy.
  Spring serves `frontend/dist/` externally; build it before production-mode verification.
- Scanner changes: `python3 -m unittest discover -s tests -v`.
- SSH or fixture changes: also run
  `SPORESYNC_SSH_TEST=1 python3 -m unittest discover -s tests -v` (requires Docker).
- Runtime/recovery changes: after building frontend and backend, run
  `python3 scripts/verify-runtime.py` from the root (Java 25, Docker and
  ssh-keygen required). It kills only its own disposable backend process.
- Container changes: run `docker build -t sporesync:verify .` and
  `bash scripts/verify-container.sh sporesync:verify`. The tag-only
  `.github/workflows/container.yml` runs CI checks, builds and verifies the image,
  then publishes it to GHCR under the Git tag. Keep image builds off PR/branch triggers.
- Packaging changes: run `scripts/package.sh OUTPUT_DIRECTORY`, extract the
  archive, and verify `scripts/start.sh` serves built assets and `/api/status`
  against disposable PostgreSQL. See `docs/deployment.md`.
- Report commands and results; never present skipped integration tests as passed.
- Update this file and README when setup or testing commands change.

Aim for 200–400 changed lines per implementation PR; keep each under 500.
The initial repository reset is the authorized exception. Use conventional
commits. PR descriptions must identify the agent, model, provider, and reasoning
setting when available. Do not commit credentials or generated artifacts.

## Transfer platform requirements

The SFTP downloader supports Linux and macOS using JNA for descriptor-relative
POSIX storage. Linux needs `/proc/self/fd`; macOS needs `/dev/fd` for metadata.
Temporary-file publication probes hard-link support before connecting. The
seedbox must have Python 3 for checksum verification; hashing heartbeats use the
configured SSH inactivity timeout. Keep platform-specific flags named and verify
native storage changes on macOS locally and Linux CI.
