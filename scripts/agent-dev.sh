#!/usr/bin/env bash
set -euo pipefail

# Starts SporeSync for automated / browser-driven feature testing (see AGENTS.md).
#
# Uses the "SporeSync.Web Agent" launch profile:
#   - HTTP only (no self-signed TLS warnings for browser automation)
#   - no browser auto-launch
#   - Testcontainers PostgreSQL (fresh database, migrations applied on boot)
#   - Testcontainers SFTP server pre-seeded with sample files
#   - Vite dev server started through the SPA proxy
#
# URLs once ready:
#   App (via Vite dev server):  http://localhost:5173
#   Backend API:                http://localhost:5040
#   Readiness probe:            http://localhost:5040/healthz/ready
#   API docs (Scalar):          http://localhost:5040/scalar/v1
#
# The SFTP connection details (mapped port, demo credentials) are printed in
# the startup log line "Development SFTP server ready".
#
# Downloads are rooted at .agent-downloads/ in the repo (gitignored) so sync
# results are easy to find and assert on. Job destination paths must be
# absolute paths inside that root, e.g. "$(pwd)/.agent-downloads/my-job".

cd "$(dirname "$0")/.."

export SporeSync__DestinationRootPath="${SPORESYNC_AGENT_DOWNLOADS:-$(pwd)/.agent-downloads}"
mkdir -p "$SporeSync__DestinationRootPath"
echo "Download root: $SporeSync__DestinationRootPath"

if ! docker info >/dev/null 2>&1; then
  echo "error: Docker must be running (needed for the PostgreSQL and SFTP test containers)." >&2
  exit 1
fi

if [[ ! -d SporeSync.Web/ClientApp/node_modules ]]; then
  echo "Installing frontend dependencies (first run)..."
  npm ci --prefix SporeSync.Web/ClientApp
fi

exec dotnet run --project SporeSync.Web/SporeSync.Web.csproj --launch-profile "SporeSync.Web Agent"
