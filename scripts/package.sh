#!/bin/sh
set -eu
repository=$(CDPATH= cd -- "$(dirname -- "$0")/.." && pwd)
if [ "$#" -ne 1 ]; then
  echo "Usage: scripts/package.sh OUTPUT_DIRECTORY" >&2
  exit 1
fi
mkdir -p "$1"
output=$(CDPATH= cd -- "$1" && pwd)
revision=$(git -C "$repository" rev-parse --short HEAD)
archive="$output/sporesync-$revision.tar.gz"
if [ -e "$archive" ]; then
  echo "Archive already exists: $archive" >&2
  exit 1
fi
(cd "$repository/frontend" && npm ci && npm run build)
(cd "$repository/backend" && "${ELIDE_BIN:-elide}" build)
# Ship source plus built static assets; the target compiles with its native Elide runtime.
tar -czf "$archive" -C "$repository" \
  backend/elide.pkl backend/.elideversion backend/config backend/src/main \
  scanner/inventory.py frontend/dist scripts/start.sh docs README.md
printf '%s\n' "$archive"
