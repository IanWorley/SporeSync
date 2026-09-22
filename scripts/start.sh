#!/bin/sh
set -eu
repository=$(CDPATH= cd -- "$(dirname -- "$0")/.." && pwd)
: "${SPRING_DATASOURCE_URL:?Set the PostgreSQL JDBC URL}"
: "${SPRING_DATASOURCE_USERNAME:?Set the PostgreSQL username}"
: "${SPRING_DATASOURCE_PASSWORD:?Set the PostgreSQL password}"
cd "$repository/backend"
java="${JAVA_HOME:+$JAVA_HOME/bin/}java"
exec "$java" -jar build/libs/sporesync.jar
