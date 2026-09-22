#!/usr/bin/env bash
set -euo pipefail

readonly IMAGE="${1:?Usage: bash scripts/verify-container.sh IMAGE}"
readonly PROJECT="sporesync-verify-$(date +%s)-$$"
readonly STARTUP_ATTEMPTS=90
readonly POLL_SECONDS=2
readonly POSTGRES_IMAGE=postgres:17-alpine
readonly DATABASE_PASSWORD=disposable-verification-password

# Only resources created by this invocation are removed.
trap 'docker logs "$PROJECT-app" 2>/dev/null || true; docker rm -f -v "$PROJECT-app" "$PROJECT-db" >/dev/null 2>&1 || true; docker network rm "$PROJECT" >/dev/null 2>&1 || true' EXIT

docker network create "$PROJECT" >/dev/null
docker run --detach --name "$PROJECT-db" --network "$PROJECT" \
  --network-alias database \
  --env POSTGRES_DB=sporesync --env POSTGRES_USER=sporesync \
  --env POSTGRES_PASSWORD="$DATABASE_PASSWORD" "$POSTGRES_IMAGE" >/dev/null
for ((attempt=0; attempt<STARTUP_ATTEMPTS; attempt++)); do
  if docker exec "$PROJECT-db" pg_isready -U sporesync -d sporesync >/dev/null 2>&1; then
    break
  fi
  sleep "$POLL_SECONDS"
done
docker exec "$PROJECT-db" pg_isready -U sporesync -d sporesync

docker run --detach --name "$PROJECT-app" --network "$PROJECT" \
  --publish 127.0.0.1::8080 \
  --env SPRING_DATASOURCE_URL=jdbc:postgresql://database:5432/sporesync \
  --env SPRING_DATASOURCE_USERNAME=sporesync \
  --env SPRING_DATASOURCE_PASSWORD="$DATABASE_PASSWORD" "$IMAGE" >/dev/null
port=$(docker port "$PROJECT-app" 8080/tcp)
readonly BASE_URL="http://$port"
for ((attempt=0; attempt<STARTUP_ATTEMPTS; attempt++)); do
  if curl --fail --silent "$BASE_URL/api/status" >/dev/null; then
    break
  fi
  sleep "$POLL_SECONDS"
done

python3 - "$BASE_URL" <<'PY'
import json
import re
import sys
from urllib.request import urlopen

base = sys.argv[1]
with urlopen(base + '/api/status') as response:
    assert json.load(response) == {'application': 'sporesync'}
with urlopen(base + '/') as response:
    assert 'text/html' in response.headers['Content-Type']
    html = response.read().decode()
    assert '<div id="root">' in html
assets = re.findall(r'(?:src|href)="(/assets/[^"\s]+)"', html)
assert any(path.endswith('.js') for path in assets), assets
assert any(path.endswith('.css') for path in assets), assets
for path in assets:
    with urlopen(base + path) as response:
        assert 'text/html' not in response.headers['Content-Type'], path
        assert response.read(), path
with urlopen(base + '/api/settings') as response:
    assert isinstance(json.load(response), dict)
print('Verified container: dashboard HTML, JavaScript, CSS, status and settings APIs.')
PY
