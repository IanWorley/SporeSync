# Keep these in step with .elideversion, the Elide image's Ubuntu base, frontend/.node-version, and
# packageManager in frontend/package.json.
ARG ELIDE_VERSION=1.6.0
ARG UBUNTU_VERSION=25.10
ARG NODE_VERSION=24.22.0
ARG BUN_VERSION=1.3.14

FROM elide.dev/elide:${ELIDE_VERSION} AS elide-dist

# The arm64 variant of elide.dev/elide:1.6.0 ships an x86-64 Ubuntu that cannot run its arm64 Elide binaries.
FROM ubuntu:${UBUNTU_VERSION} AS elide
COPY --from=elide-dist /opt/elide /opt/elide
ENV PATH=/opt/elide/bin:$PATH

FROM oven/bun:${BUN_VERSION} AS bun

# Elide 1.6.0 cannot run Vite, so only this build stage uses Node.
FROM node:${NODE_VERSION}-slim AS frontend
COPY --from=bun /usr/local/bin/bun /usr/local/bin/bun
WORKDIR /src/frontend
COPY frontend/package.json frontend/bun.lock ./
RUN bun install --frozen-lockfile
COPY frontend/ ./
RUN node_modules/.bin/vp build

FROM elide AS backend
WORKDIR /src
COPY .elideversion elide.pkl ./
COPY tools/ tools/
COPY backend/ backend/
WORKDIR /src/backend
# `elide install` writes an empty runtime classpath when backend/src is missing, so it runs after the copy.
RUN elide install && elide build
RUN mkdir -p /build/lib \
    && tr ':' '\n' < .dev/dependencies/m2/runtime.classpath | xargs cp -L -t /build/lib \
    && elide jar -- --create --file /build/lib/sporesync.jar \
        -C .dev/jvm/classes/main/kotlin . -C .dev/staging/jvm-resources/main .

FROM elide
ARG APP_DIR=/opt/sporesync
ARG CONFIG_DIR=/config
ARG DOWNLOADS_DIR=/downloads
WORKDIR ${APP_DIR}
COPY --from=backend /build/lib lib/
COPY --from=frontend /src/frontend/dist frontend/
# Docker gives a new named volume this ownership, so `ubuntu` (UID 1000 in the base image) can write to it.
RUN mkdir "${CONFIG_DIR}" "${DOWNLOADS_DIR}" && chown ubuntu:ubuntu "${CONFIG_DIR}" "${DOWNLOADS_DIR}"
USER ubuntu
ENV SERVER_PORT=8080 \
    SPORESYNC_SETTINGSFILE=${CONFIG_DIR}/settings.yaml \
    SPORESYNC_PERSISTENCE_DIRECTORY=${CONFIG_DIR} \
    SPORESYNC_PATHS_LOCALDIR=${DOWNLOADS_DIR} \
    SPORESYNC_FRONTEND_DIST=${APP_DIR}/frontend \
    SPORESYNC_FRONTEND_REQUIRED=true
VOLUME ${CONFIG_DIR} ${DOWNLOADS_DIR}
EXPOSE ${SERVER_PORT}
# `elide java` exits on SIGTERM without passing it to its JVM, so `docker stop` would kill SporeSync before H2 closes.
ENTRYPOINT ["/opt/elide/bin/java.bin", "-cp", "lib/*", "com.sporesync.SporeSyncApplicationKt"]
