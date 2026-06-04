# SporeSync

SporeSync is an ASP.NET Core application with a React/Vite single-page app for monitoring and managing SFTP sync jobs. The backend exposes REST APIs, SignalR dashboard updates, database migrations, and API documentation. The frontend lives inside the web project under `SporeSync.Web/ClientApp`.

## Features

- ASP.NET Core web API and static SPA hosting
- React, TypeScript, Vite, TanStack Router, and TanStack Query frontend
- SignalR dashboard updates
- PostgreSQL persistence with FluentMigrator migrations
- Real SFTP sync worker: scheduled job polling, incremental scan/enqueue, serial download to local filesystem
- Backend-driven first-child opaque folder grouping: large directories appear as a small number of logical rows in the dashboard with subtree aggregates (see docs/folder-grouping-implementation-plan.html and docs/grouping-rules.md)
- Scalar/OpenAPI API documentation
- Optional Testcontainers-backed PostgreSQL development profile

## Prerequisites

- .NET SDK 10.0
- Node.js 24.x
- npm
- PostgreSQL, unless using the Testcontainers launch profile
- Docker, only required for the Testcontainers profile or container publishing

## Getting Started

Clone the repository and restore dependencies:

```bash
git clone <repo-url>
cd sporesync
dotnet restore SporeSync.sln
npm ci --prefix SporeSync.Web/ClientApp
```

## Running The App

### Full-stack development

The web project is configured with ASP.NET Core SPA proxy. This starts the Vite dev server automatically and opens the app through the ASP.NET Core launch profile.

```bash
dotnet run --project SporeSync.Web/SporeSync.Web.csproj
```

Default local URLs:

- Web app: `https://localhost:7040`
- HTTP fallback: `http://localhost:5040`
- Vite dev server: `http://localhost:5173`
- API docs: `https://localhost:7040/scalar/v1`
- OpenAPI JSON: `https://localhost:7040/openapi/v1.json`

### Full-stack development with Testcontainers

Use this profile when you want the app to start its own PostgreSQL container:

```bash
dotnet run --project SporeSync.Web/SporeSync.Web.csproj --launch-profile "SporeSync.Web Testcontainer"
```

This requires Docker to be running.

### Frontend-only development

You can also run Vite directly. The Vite dev server proxies `/api`, `/hubs`, `/openapi`, and `/scalar` to the ASP.NET Core backend.

```bash
dotnet run --project SporeSync.Web/SporeSync.Web.csproj
npm run dev --prefix SporeSync.Web/ClientApp
```

Open `http://localhost:5173`.

## Configuration

The default connection string is in `SporeSync.Web/appsettings.json` and `SporeSync.Web/appsettings.Development.json`:

```json
{
  "ConnectionStrings": {
    "DefaultConnection": "Host=localhost;Port=5432;Database=SporeSync;Username=sporesync;Password=sporesync"
  }
}
```

For local development without Testcontainers, create a matching PostgreSQL database and user or override `ConnectionStrings:DefaultConnection` with environment variables, user secrets, or local configuration.

Configure an SFTP connection profile and sync job through the admin UI or REST API (`/api/sftp-connection-profiles`, `/api/sftp-sync-jobs`). Trigger an immediate run with `POST /api/sftp-sync-jobs/{id}/run`. Enabled jobs are polled automatically every 10 seconds (configurable via `SporeSync:SchedulerIntervalSeconds`).

Worker settings in `appsettings.json`:

```json
{
  "SporeSync": {
    "SchedulerIntervalSeconds": 10,
    "DownloadPollIntervalMs": 1000,
    "SftpConnectionTimeoutSeconds": 30,
    "SftpOperationTimeoutSeconds": 300
  }
}
```

## Common Commands

```bash
# Restore .NET packages
dotnet restore SporeSync.sln

# Build everything
dotnet build SporeSync.sln

# Run backend and SPA through SpaProxy
dotnet run --project SporeSync.Web/SporeSync.Web.csproj

# Run backend with a Testcontainers PostgreSQL database
dotnet run --project SporeSync.Web/SporeSync.Web.csproj --launch-profile "SporeSync.Web Testcontainer"

# Run .NET tests
dotnet test SporeSync.sln

# Install frontend packages
npm ci --prefix SporeSync.Web/ClientApp

# Run frontend lint
npm run lint --prefix SporeSync.Web/ClientApp

# Run frontend tests
npm run test --prefix SporeSync.Web/ClientApp

# Build frontend
npm run build --prefix SporeSync.Web/ClientApp

# Publish the web app
dotnet publish SporeSync.Web/SporeSync.Web.csproj --configuration Release --output ./artifacts/publish

# Update the changelog from git history
scripts/update-changelog.sh

# Create a release changelog section locally
scripts/update-changelog.sh --version 0.1.0
```

`dotnet publish` runs `npm ci` and `npm run build` for `SporeSync.Web/ClientApp`, then publishes the built SPA assets from `SporeSync.Web/wwwroot`.

## Project Structure

```text
SporeSync.Domain/              Domain models and repository contracts
SporeSync.Business/            Application services and business rules
SporeSync.Infrastructure/      PostgreSQL repositories and migrations
SporeSync.Web/                 ASP.NET Core API, SignalR hubs, SPA hosting
SporeSync.Web/ClientApp/       React/Vite frontend
SporeSync.Business.Tests/      .NET test project
docs/                         Project notes and implementation docs
```

## Development Notes

- The backend runs database migrations on startup.
- In development, Scalar is available at `/scalar/v1`.
- The SPA uses relative API URLs, so it works both behind Vite's dev proxy and when served from ASP.NET Core after publish.
- `SporeSync.Web/wwwroot` is generated by the frontend build and is ignored by git.
- `SporeSync.Web/ClientApp/node_modules` is ignored by git.

## Contributing

1. Create a branch for your change.
2. Keep changes focused and consistent with the existing project structure.
3. Run the relevant checks before opening a pull request:

```bash
dotnet build SporeSync.sln
dotnet test SporeSync.sln
npm run lint --prefix SporeSync.Web/ClientApp
npm run test --prefix SporeSync.Web/ClientApp
npm run build --prefix SporeSync.Web/ClientApp
```

4. Include tests or update existing tests when changing behavior.
5. Update documentation when setup, commands, configuration, or public behavior changes.

## CI/CD

GitHub Actions restores .NET and Node dependencies, validates the changelog generator, runs frontend lint/tests/build, builds the solution, runs .NET tests with coverage collection, publishes a web artifact, updates `CHANGELOG.md` from git history after successful main/master builds, and can publish a container image to GitHub Container Registry on tags.
Run the CI/CD workflow manually with `changelog_version` before tagging to create a release changelog section.
