# Agent Instructions

SporeSync is an ASP.NET Core (.NET 10) backend with a React/Vite SPA in
`SporeSync.Web/ClientApp`. It syncs files from SFTP servers to the local
filesystem: profiles and jobs are managed through the UI/REST API, a scheduler
scans remote directories, and a download worker fetches changed files.

## Build and automated tests

```bash
dotnet build SporeSync.sln                          # build everything
dotnet test SporeSync.sln                           # .NET tests (needs Docker: Testcontainers PostgreSQL + SFTP)
npm run biome:check --prefix SporeSync.Web/ClientApp # frontend lint/format check
npm test --prefix SporeSync.Web/ClientApp            # frontend unit tests (Vitest)
```

Run `npm ci --prefix SporeSync.Web/ClientApp` once before frontend commands.

Required checks before handing work back:

- Backend changes: `dotnet build SporeSync.sln` and `dotnet test SporeSync.sln`.
- UI changes: `npm run biome:check` and `npm test` from `SporeSync.Web/ClientApp`.
- Report the commands run and whether they passed.

The SFTP end-to-end integration tests live in
`SporeSync.Business.Tests/Sftp/` and exercise the real pipeline against
`atmoz/sftp` and PostgreSQL containers. Prefer extending those over mocks when
testing sync behavior.

## Feature testing with a real browser (Chrome MCP / browser automation)

Use this when you need to verify a feature end to end through the UI.

### 1. Start the app in agent mode

```bash
scripts/agent-dev.sh
```

This runs the `SporeSync.Web Agent` launch profile (equivalent to
`dotnet run --project SporeSync.Web/SporeSync.Web.csproj --launch-profile "SporeSync.Web Agent"`).
It is designed for automation:

- HTTP only on `http://localhost:5040` — no self-signed TLS certificate
  warnings to click through.
- No browser auto-launch.
- A fresh Testcontainers PostgreSQL database; migrations run on boot.
- A Testcontainers SFTP server (`atmoz/sftp`) pre-seeded with sample files
  (`/upload/welcome.txt`, `/upload/reports/2026/*.csv`,
  `/upload/media/show-one/*.mkv`).
- The Vite dev server on `http://localhost:5173`, proxying `/api`, `/hubs`,
  `/openapi`, and `/scalar` to the backend.

Docker must be running. First startup pulls container images and can take a
minute or two; run the script in the background and poll readiness.

### 2. Wait until ready

```bash
curl -fsS http://localhost:5040/healthz/ready   # 200 "Healthy" when up
```

Then find the SFTP connection details in the app's startup output — look for
the log line:

```text
Development SFTP server ready: host=localhost port=<mapped-port> username=demo password=demo-password remote path=/upload
```

### 3. Drive the UI

Point the browser (Chrome MCP, Playwright, or similar) at
`http://localhost:5173`. Typical feature-test flow:

1. Open the admin pages and create an SFTP connection profile using the
   host/port/credentials from the startup log (password auth).
2. Create a sync job for that profile with source path `/upload`. The
   destination path must be an absolute path inside the download root —
   `scripts/agent-dev.sh` sets the root to `<repo>/.agent-downloads/`
   (gitignored), so use e.g. `<repo>/.agent-downloads/my-job`.
3. Trigger a run from the UI (or `POST /api/sftp-sync-jobs/{id}/run`) and
   watch the dashboard update live over SignalR.
4. Verify downloaded files on disk under `.agent-downloads/`.

To create new remote test content mid-session, exec into the SFTP container
(`docker ps` shows the `atmoz/sftp` container; files live under
`/home/demo/upload/`), or restart with fresh seed data.

### 4. API-first alternative

Everything the UI does is available over REST — useful for setup steps before
a UI assertion, or when a browser is unnecessary:

- Interactive docs: `http://localhost:5040/scalar/v1`
- OpenAPI JSON: `http://localhost:5040/openapi/v1.json`
- Key endpoints: `/api/sftp-connection-profiles`, `/api/sftp-sync-jobs`,
  `/api/sftp-sync-jobs/{id}/run`, `/api/sftp-sync-runs`, `/api/status`

### 5. Clean up

Stop the app (Ctrl+C or kill the `dotnet run` process). Testcontainers removes
the PostgreSQL and SFTP containers automatically.

## Conventions

- Only submit pull requests with fewer than 500 changed lines of code. If a
  change requires 500 lines or more, split it into separate issues and
  feature branches so each pull request is easier for humans to review.
- Do not commit `SporeSync.Web/wwwroot/` (generated) or
  `SporeSync.Web/ClientApp/node_modules/`.
- The backend runs FluentMigrator migrations on startup; add schema changes as
  new migrations in `SporeSync.Infrastructure`.
- Update `README.md` and this file when setup, commands, or testing workflows
  change.
