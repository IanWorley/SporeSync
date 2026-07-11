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
      Auth__Username: admin
      Auth__PasswordHash: "change-this-to-a-generated-password-hash"
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

Set `Auth__PasswordHash` to a hash generated with
`dotnet run --project SporeSync.Web/SporeSync.Web.csproj -- hash-password`;
the container refuses to start without an admin credential. See
[`authentication.md`](authentication.md) for the full authentication
configuration, including how to disable login on a trusted network.

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

## TLS reverse proxy headers

If a production reverse proxy terminates TLS and forwards HTTP to the web
container, enable forwarded headers so SporeSync sees the original HTTPS scheme
and client IP address. This is required for `Secure` auth cookies, login rate
limiting, and audit logs to behave as if the request reached the app directly
over HTTPS.

Do not enable forwarded headers without also configuring the trusted proxy IP
or network. The app rejects that configuration at startup because otherwise a
client could spoof `X-Forwarded-For` or `X-Forwarded-Proto`.

Example for a reverse proxy on an explicit Docker network:

```yaml
services:
  web:
    environment:
      ForwardedHeaders__Enabled: "true"
      ForwardedHeaders__KnownNetworks__0: "172.30.80.0/24"
      ForwardedHeaders__ForwardLimit: "1"
    networks:
      - sporesync-edge
      - default

networks:
  sporesync-edge:
    ipam:
      config:
        - subnet: 172.30.80.0/24
```

Use `ForwardedHeaders__KnownProxies__0` instead of `KnownNetworks` when the
reverse proxy has a stable single IP address, for example:

```yaml
environment:
  ForwardedHeaders__Enabled: "true"
  ForwardedHeaders__KnownProxies__0: "172.30.80.10"
  ForwardedHeaders__ForwardLimit: "1"
```

Configure the proxy to send `X-Forwarded-For` and `X-Forwarded-Proto`. Trust
only the Docker subnet or proxy address that can connect directly to the web
container.
