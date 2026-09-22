# syntax=docker/dockerfile:1
FROM node:24-bookworm-slim AS frontend
WORKDIR /build/frontend
COPY frontend/package*.json ./
RUN npm ci
COPY frontend/ ./
RUN npm run build

FROM eclipse-temurin:17-jdk-noble AS plugin-java
FROM eclipse-temurin:25-jdk-noble AS backend
RUN apt-get update && apt-get install -y --no-install-recommends curl perl unzip \
    && rm -rf /var/lib/apt/lists/*
COPY --from=plugin-java /opt/java/openjdk /opt/java/jdk17
WORKDIR /build/backend
COPY backend/ ./
RUN bash scripts/prepare-elide-plugin.sh
RUN --mount=type=cache,target=/root/.gradle \
    ./gradlew --no-daemon -Porg.gradle.java.installations.paths=/opt/java/jdk17,/opt/java/openjdk bootJar

FROM eclipse-temurin:25-jre-noble AS runtime
LABEL org.opencontainers.image.source="https://github.com/IanWorley/SporeSync"
ARG APP_UID=10001
RUN useradd --uid "$APP_UID" --create-home sporesync \
    && mkdir -p /downloads && chown sporesync:sporesync /downloads
WORKDIR /app/backend
COPY --from=backend /build/backend/build/libs/sporesync.jar build/libs/sporesync.jar
COPY backend/config/ config/
COPY --from=frontend /build/frontend/dist/ /app/frontend/dist/
COPY scanner/inventory.py /app/scanner/inventory.py
ENV SERVER_ADDRESS=0.0.0.0
EXPOSE 8080
USER sporesync
ENTRYPOINT ["java", "-jar", "build/libs/sporesync.jar"]
