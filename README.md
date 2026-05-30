# SftpSync

SftpSync is an ASP.NET Core application with a React/Vite single-page app for monitoring and managing SFTP sync jobs. The backend exposes REST APIs, SignalR dashboard updates, database migrations, and API documentation. The frontend lives inside the web project under `SftpSync.Web/ClientApp`.

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
cd sftpsync
dotnet restore SftpSync.sln
npm ci --prefix SftpSync.Web/ClientApp
```

## Running The App

### Full-stack development

The web project is configured with ASP.NET Core SPA proxy. This starts the Vite dev server automatically and opens the app through the ASP.NET Core launch profile.

```bash
dotnet run --project SftpSync.Web/SftpSync.Web.csproj
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
dotnet run --project SftpSync.Web/SftpSync.Web.csproj --launch-profile "SftpSync.Web Testcontainer"
```

This requires Docker to be running.

### Frontend-only development

You can also run Vite directly. The Vite dev server proxies `/api`, `/hubs`, `/openapi`, and `/scalar` to the ASP.NET Core backend.

```bash
dotnet run --project SftpSync.Web/SftpSync.Web.csproj
npm run dev --prefix SftpSync.Web/ClientApp
```

Open `http://localhost:5173`.

## Configuration

The default connection string is in `SftpSync.Web/appsettings.json` and `SftpSync.Web/appsettings.Development.json`:

```json
{
  "ConnectionStrings": {
    "DefaultConnection": "Host=localhost;Port=5432;Database=SftpSync;Username=sftpsync;Password=sftpsync"
  }
}
```

For local development without Testcontainers, create a matching PostgreSQL database and user or override `ConnectionStrings:DefaultConnection` with environment variables, user secrets, or local configuration.

Configure an SFTP connection profile and sync job through the admin UI or REST API (`/api/sftp-connection-profiles`, `/api/sftp-sync-jobs`). Trigger an immediate run with `POST /api/sftp-sync-jobs/{id}/run`. Enabled jobs are polled automatically every 10 seconds (configurable via `SftpSync:SchedulerIntervalSeconds`).

Worker settings in `appsettings.json`:

```json
{
  "SftpSync": {
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
dotnet restore SftpSync.sln

# Build everything
dotnet build SftpSync.sln

# Run backend and SPA through SpaProxy
dotnet run --project SftpSync.Web/SftpSync.Web.csproj

# Run backend with a Testcontainers PostgreSQL database
dotnet run --project SftpSync.Web/SftpSync.Web.csproj --launch-profile "SftpSync.Web Testcontainer"

# Run .NET tests
dotnet test SftpSync.sln

# Install frontend packages
npm ci --prefix SftpSync.Web/ClientApp

# Run frontend lint
npm run lint --prefix SftpSync.Web/ClientApp

# Run frontend tests
npm run test --prefix SftpSync.Web/ClientApp

# Build frontend
npm run build --prefix SftpSync.Web/ClientApp

# Publish the web app
dotnet publish SftpSync.Web/SftpSync.Web.csproj --configuration Release --output ./artifacts/publish
```

`dotnet publish` runs `npm ci` and `npm run build` for `SftpSync.Web/ClientApp`, then publishes the built SPA assets from `SftpSync.Web/wwwroot`.

## Project Structure

```text
SftpSync.Domain/              Domain models and repository contracts
SftpSync.Business/            Application services and business rules
SftpSync.Infrastructure/      PostgreSQL repositories and migrations
SftpSync.Web/                 ASP.NET Core API, SignalR hubs, SPA hosting
SftpSync.Web/ClientApp/       React/Vite frontend
SftpSync.Business.Tests/      .NET test project
docs/                         Project notes and implementation docs
```

## Development Notes

- The backend runs database migrations on startup.
- In development, Scalar is available at `/scalar/v1`.
- The SPA uses relative API URLs, so it works both behind Vite's dev proxy and when served from ASP.NET Core after publish.
- `SftpSync.Web/wwwroot` is generated by the frontend build and is ignored by git.
- `SftpSync.Web/ClientApp/node_modules` is ignored by git.

## Contributing

1. Create a branch for your change.
2. Keep changes focused and consistent with the existing project structure.
3. Run the relevant checks before opening a pull request:

```bash
dotnet build SftpSync.sln
dotnet test SftpSync.sln
npm run lint --prefix SftpSync.Web/ClientApp
npm run test --prefix SftpSync.Web/ClientApp
npm run build --prefix SftpSync.Web/ClientApp
```

4. Include tests or update existing tests when changing behavior.
5. Update documentation when setup, commands, configuration, or public behavior changes.

## CI/CD

GitHub Actions restores .NET and Node dependencies, runs frontend lint/tests/build, builds the solution, runs .NET tests with coverage collection, publishes a web artifact, and can publish a container image to GitHub Container Registry on pushes.
