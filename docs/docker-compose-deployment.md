# Deploying with Docker Compose

SporeSync can run as a published ASP.NET Core container with PostgreSQL in a
Docker Compose stack. The web container serves the built React app, runs
database migrations on startup, hosts the API, and runs the background SFTP sync
workers.

## Prerequisites

- Docker Engine with Docker Compose v2
- A published SporeSync container image, or a local image built with
  `dotnet publish /t:PublishContainer`
- A host directory where synced downloads should be written

## Build or choose an image

Release tags publish images to GitHub Container Registry. Replace
`OWNER/REPOSITORY` and `VERSION` with the repository path and release version:

```bash
docker pull ghcr.io/OWNER/REPOSITORY:VERSION
```

To build an image locally from the repository instead:

```bash
dotnet publish SporeSync.Web/SporeSync.Web.csproj \
  --configuration Release \
  /t:PublishContainer \
  -p:ContainerRepository=sporesync-web \
  -p:ContainerImageTag=local
```

Use `sporesync-web:local` as the Compose image name after the local build.

## Compose file

Create `compose.yml` on the deployment host:

```yaml
services:
  web:
    image: ghcr.io/OWNER/REPOSITORY:VERSION
    depends_on:
      postgres:
        condition: service_healthy
    ports:
      - "8080:8080"
    environment:
      ASPNETCORE_URLS: http://+:8080
      ConnectionStrings__DefaultConnection: Host=postgres;Port=5432;Database=SporeSync;Username=sporesync;Password=change-this-database-password
      Security__EncryptionKeyPath: /var/lib/sporesync/secrets/encryption.key
      SporeSync__DestinationRootPath: /downloads
      SporeSync__SchedulerIntervalSeconds: 10
      SporeSync__DownloadPollIntervalMs: 1000
      SporeSync__SftpConnectionTimeoutSeconds: 30
      SporeSync__SftpOperationTimeoutSeconds: 300
    volumes:
      - sporesync-secrets:/var/lib/sporesync/secrets
      - ./downloads:/downloads
    restart: unless-stopped

  postgres:
    image: postgres:17
    environment:
      POSTGRES_DB: SporeSync
      POSTGRES_USER: sporesync
      POSTGRES_PASSWORD: change-this-database-password
    volumes:
      - postgres-data:/var/lib/postgresql/data
    healthcheck:
      test: ["CMD-SHELL", "pg_isready -U sporesync -d SporeSync"]
      interval: 10s
      timeout: 5s
      retries: 5
    restart: unless-stopped

volumes:
  postgres-data:
  sporesync-secrets:
```

Change both database password values to the same strong password before starting
the stack. If you built the image locally, replace the `web.image` value with
`sporesync-web:local`.

## Start the stack

From the directory containing `compose.yml`:

```bash
docker compose up -d
docker compose logs -f web
```

Open `http://localhost:8080` when running on the same host, or replace
`localhost` with the server name or IP address.

## Persistent data

Keep these mounts when upgrading or recreating containers:

- `postgres-data` stores application data and migration history.
- `sporesync-secrets` stores the first-boot encryption key used for SFTP
  credentials.
- `./downloads` receives downloaded files from sync jobs.

The encryption key is intentionally outside the database. After first boot, the
application refuses to start if the database says first-run initialization
already completed but the key file is missing. Back up the `sporesync-secrets`
volume with the database, and restore both together.

## Upgrades

Update the image tag and restart the web container:

```bash
docker compose pull web
docker compose up -d web
docker compose logs -f web
```

Database migrations run automatically during web startup.

## Configuration notes

- The API documentation UI is only enabled when `ASPNETCORE_ENVIRONMENT` is
  `Development`; production compose deployments normally leave it disabled.
- The default download root inside the container is `/downloads` in the sample.
  Use a bind mount or named volume that matches `SporeSync__DestinationRootPath`.
- If the app must connect to SFTP hosts on a private network, make sure the
  Docker host and container network can resolve and reach those hosts.
- When running behind a reverse proxy, terminate TLS at the proxy and forward
  traffic to the web container on port `8080`.
