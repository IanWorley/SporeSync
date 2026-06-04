# Contributing to SporeSync

Thanks for taking the time to contribute. Keep changes focused, match the existing project structure, and include tests or documentation updates when behavior changes.

## Development Setup

Install the required toolchains:

- .NET SDK 10.0
- Node.js 24.x
- npm
- PostgreSQL, unless using the Testcontainers launch profile
- Docker, only required for Testcontainers or container publishing

Restore dependencies:

```bash
dotnet restore SporeSync.sln
npm ci --prefix SporeSync.Web/ClientApp
```

## Branches

Create a branch for each change:

```bash
git switch -c chore/example-change
```

Use short branch names that describe the work, such as `fix/run-status`, `feat/profile-validation`, or `docs/setup-notes`.

## Project Layout

```text
SporeSync.Domain/              Domain models and repository contracts
SporeSync.Business/            Application services and business rules
SporeSync.Infrastructure/      PostgreSQL repositories and migrations
SporeSync.Web/                 ASP.NET Core API, SignalR hubs, SPA hosting
SporeSync.Web/ClientApp/       React/Vite frontend
SporeSync.Business.Tests/      .NET test project
docs/                         Project notes and implementation docs
```

The public product name is SporeSync. Internal namespaces, project paths, API routes, and configuration keys still use `SporeSync`.

## Checks

Run the relevant checks before opening a pull request:

```bash
dotnet build SporeSync.sln
dotnet test SporeSync.sln
npm run lint --prefix SporeSync.Web/ClientApp
npm run test --prefix SporeSync.Web/ClientApp
npm run build --prefix SporeSync.Web/ClientApp
```

After changing UI code, run these commands from `SporeSync.Web/ClientApp`:

```bash
npm run biome:check
npm test
```

## Changelog

`CHANGELOG.md` is generated from git history. To refresh the Unreleased section:

```bash
scripts/update-changelog.sh
```

To prepare a release section before tagging:

```bash
scripts/update-changelog.sh --version 0.1.0
git add CHANGELOG.md
git commit -m "docs: update changelog for 0.1.0"
git tag v0.1.0
```

The CI/CD workflow refreshes `CHANGELOG.md` automatically after successful pushes to `main`/`master`. It can also be run manually with `changelog_version` and `changelog_date` inputs to create a release section before tagging.

## Pull Requests

Before submitting:

1. Keep the change scoped to one purpose.
2. Include or update tests for behavior changes.
3. Update README or docs when setup, commands, configuration, or public behavior changes.
4. Mention any checks you could not run.
5. Avoid committing generated output such as `SporeSync.Web/wwwroot` or `node_modules`.

## Local Development

Run the full-stack app through the ASP.NET Core SPA proxy:

```bash
dotnet run --project SporeSync.Web/SporeSync.Web.csproj
```

Run with Testcontainers-backed PostgreSQL:

```bash
dotnet run --project SporeSync.Web/SporeSync.Web.csproj --launch-profile "SporeSync.Web Testcontainer"
```

Run the frontend dev server directly:

```bash
dotnet run --project SporeSync.Web/SporeSync.Web.csproj
npm run dev --prefix SporeSync.Web/ClientApp
```
