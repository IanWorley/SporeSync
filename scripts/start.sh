#!/bin/sh
set -eu
repository=$(CDPATH= cd -- "$(dirname -- "$0")/.." && pwd)
: "${SPRING_DATASOURCE_URL:?Set the PostgreSQL JDBC URL}"
: "${SPRING_DATASOURCE_USERNAME:?Set the PostgreSQL username}"
: "${SPRING_DATASOURCE_PASSWORD:?Set the PostgreSQL password}"
cd "$repository/backend"
exec "${ELIDE_BIN:-elide}" run
