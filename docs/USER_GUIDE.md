# SporeSync User Guide

SporeSync mirrors files from a remote seedbox to your machine over SSH, with a web UI for browsing, queuing, extracting, and deleting files. It is a rewrite of [seedsync](https://github.com/nitrobass24/seedsync).

This guide is based on the [SporeSync specification](SPEC.md). SporeSync is still being built, so parts of this guide describe planned behavior.

## How to read this guide

- **Specified** means the behavior is decided in the spec.
- **Not yet established** means the spec leaves the detail to the implementation and the implementation has not decided it yet, such as where the image is published. These are shown as `<placeholders>`. This guide will be updated when the implementation decides them.

## Before you start

- **No login in v1.** SporeSync has no authentication and no multi-user support. Run it only on a trusted network, and do not expose its web UI to the internet.
- **One remote host.** v1 syncs from a single seedbox.
- **Python 3 on the remote host.** See [Remote host requirements](#remote-host-requirements).
- **Docker** on the local machine. Docker is the primary way to run SporeSync.

## Docker setup

SporeSync ships as a single Docker image. The image runs on Elide, the only runtime it uses. You need only Docker to build and run it. You do not need Java, Node.js, or anything else installed locally.

### Build the image

The image is not published to a registry yet. Build it from a checkout of this repository:

```sh
docker build -t sporesync .
```

The image builds on `amd64` and `arm64` hosts.

### Volumes

| Container path | Holds | Notes |
|----------------|-------|-------|
| `/config` | Settings file and the H2 database | Back this up. It keeps your settings, auto-queue patterns, and transfer history. |
| `/downloads` | Your local download directory | Files from the seedbox land here. SporeSync also keeps its tracking markers here, in `/downloads/.sporesync`. |

Mount both volumes to folders on the host so your data survives container upgrades.

### File ownership

SporeSync runs in the container as UID 1000 and GID 1000. It must be able to write to both volumes.

- Create the host folders before the first start. If a folder is missing, Docker creates it owned by root, and SporeSync cannot write to it.
- To have your own account own the settings and downloads, pass `--user "$(id -u):$(id -g)"`. In Compose, set `user` to the numbers that `id -u` and `id -g` print.
- A named volume needs no setup when you keep the default user. Docker gives a new named volume the ownership of the folder in the image.

### Example

The web UI listens on port `8080` in the container.

```sh
mkdir -p /path/on/host/sporesync-config /path/on/host/downloads
docker run -d \
  --name sporesync \
  --user "$(id -u):$(id -g)" \
  -p 8080:8080 \
  -v /path/on/host/sporesync-config:/config \
  -v /path/on/host/downloads:/downloads \
  sporesync
```

To use another host port, change the number before the colon, as in `-p 9090:8080`.

The same setup with Docker Compose:

```yaml
services:
  sporesync:
    image: sporesync
    user: "1000:1000"
    ports:
      - "8080:8080"
    volumes:
      - /path/on/host/sporesync-config:/config
      - /path/on/host/downloads:/downloads
    restart: unless-stopped
```

Then open <http://localhost:8080> in a browser and fill in the Settings page.

### SSH keys in Docker

If you log in to the seedbox with an SSH key, the key path setting must be a path **inside the container**. A host path such as `~/.ssh/id_ed25519` does not exist there.

The spec does not define a key location. The simplest option is to keep the key in your config folder, which is already mounted:

1. Copy the private key to `/path/on/host/sporesync-config/ssh/id_ed25519`.
2. Set the key path to `/config/ssh/id_ed25519`.

The user SporeSync runs as must be able to read the key. See [File ownership](#file-ownership).

Or mount the key read-only on its own and use that container path:

```sh
-v ~/.ssh/id_ed25519:/keys/id_ed25519:ro
```

### Health check

The container exposes a health endpoint at `/actuator/health`. It returns `"status":"UP"` once SporeSync is ready. The image has no `curl` or `wget`, so probe the endpoint from the host or from a monitoring tool:

```sh
curl http://localhost:8080/actuator/health
```

## Settings

You can change settings in three ways:

1. The **Settings** page in the web UI (`/settings`). Each field saves on its own and shows validation errors inline. Some changes show a "restart required" notice.
2. The **settings file** in `/config`, named `settings.yaml`.
3. **Environment variables**, which override values from the settings file. See [Environment overrides](#environment-overrides).

SporeSync validates settings when it loads them and when you save them. Invalid settings stop startup, and the log names each invalid field, such as `scan.remote-interval must be greater than zero`. In Docker, the container exits.

### Settings reference

The groups and settings below are specified. Each setting has a key, written as `<group>.<setting>`. In `settings.yaml`, the group is a section and the setting is a field inside it:

```yaml
general:
  debug: true
connection:
  port: 2222
```

This guide does not list types and defaults yet.

| Group | Setting | Key | What it does |
|-------|---------|-----|--------------|
| General | Debug | `general.debug` | Turns on debug logging. Logs are structured JSON. |
| General | Verbose | `general.verbose` | Turns on verbose output. |
| Connection | Host | `connection.host` | Seedbox hostname or IP address. |
| Connection | Port | `connection.port` | SSH port on the seedbox. |
| Connection | Username | `connection.username` | SSH username. |
| Connection | Auth method | `connection.auth-method` | Whether to log in with an SSH key (`KEY`) or a password (`PASSWORD`). |
| Connection | Key path | `connection.key-path` | Path to the private key, used with key auth. In Docker, use a container path. See [SSH keys in Docker](#ssh-keys-in-docker). |
| Connection | Password | `connection.password` | Password, used with password auth. |
| Paths | Remote dir | `paths.remote-dir` | Directory on the seedbox to mirror. |
| Paths | Local dir | `paths.local-dir` | Local download directory. The Docker image fixes it to `/downloads`. See [Variables the image sets](#variables-the-image-sets). |
| Paths | Remote scanner path | `paths.remote-scanner-path` | Where SporeSync places its scanner script on the seedbox. |
| Transfer | Concurrent file limit | `transfer.concurrent-file-limit` | How many files download at the same time. |
| Transfer | Per-file connection limit | `transfer.per-file-connection-limit` | How many SFTP connections one file may use. Each file can be split into byte ranges that download in parallel. |
| Transfer | Bandwidth limit | `transfer.bandwidth-limit` | Overall download speed cap. |
| Scan | Remote interval | `scan.remote-interval` | How often SporeSync scans the seedbox. |
| Scan | Local interval | `scan.local-interval` | How often SporeSync scans the local download directory. |
| Extraction | Enabled | `extraction.enabled` | Extract archives automatically after download. |
| Extraction | Delete archive after extract | `extraction.delete-archive-after-extract` | Remove the archive once extraction succeeds. |
| AutoQueue | Enabled | `autoqueue.enabled` | Queue new remote files automatically. |
| AutoQueue | Patterns-only mode | `autoqueue.patterns-only` | When on, only files matching a pattern are queued. When off, every new file is queued. |

Auto-queue patterns are not part of the settings file. You manage them on the AutoQueue page (`/autoqueue`), and they are stored in the H2 database in `/config`.

### Environment overrides

Environment variables override settings file values using Spring Boot's [relaxed binding](https://docs.spring.io/spring-boot/reference/features/external-config.html#features.external-config.typesafe-configuration-properties.relaxed-binding.environment-variables). SporeSync reads them at startup. It never writes an override to `settings.yaml`, and an override wins over a value saved on the Settings page.

To name the variable for a setting, put `sporesync.` in front of its key from the [settings reference](#settings-reference). Then apply Spring's rules:

1. Replace dots (`.`) with underscores (`_`).
2. Remove dashes (`-`).
3. Convert to uppercase.

So `connection.port` becomes `SPORESYNC_CONNECTION_PORT`, and `scan.remote-interval` becomes `SPORESYNC_SCAN_REMOTEINTERVAL`.

`LOGGING_LEVEL_COM_SPORESYNC=DEBUG` also works. It turns on debug logs for SporeSync's own code. It sets the Spring property `logging.level.com.sporesync` and follows the same rules.

In Compose, pass overrides under `environment:`:

```yaml
services:
  sporesync:
    environment:
      SPORESYNC_CONNECTION_PORT: "2222"
```

### Variables the image sets

The Docker image sets these variables so that SporeSync keeps its data in the volumes. If you point a path somewhere else, that data no longer survives a new container.

| Variable | Value in the image | What it does |
|----------|--------------------|--------------|
| `SERVER_PORT` | `8080` | Port the web UI listens on inside the container. |
| `SPORESYNC_SETTINGSFILE` | `/config/settings.yaml` | Location of the settings file. |
| `SPORESYNC_PERSISTENCE_DIRECTORY` | `/config` | Folder for the H2 database file, `sporesync.mv.db`. |
| `SPORESYNC_PATHS_LOCALDIR` | `/downloads` | The Local dir setting. Downloads land in the `/downloads` volume whatever `settings.yaml` or the Settings page says. |
| `SPORESYNC_FRONTEND_DIST` | `/opt/sporesync/frontend` | Folder that holds the built web UI. |
| `SPORESYNC_FRONTEND_REQUIRED` | `true` | Stops startup if the web UI files are missing. |

## Remote host requirements

SporeSync needs very little on the seedbox:

- **SSH access** with SFTP enabled, using a key or a password.
- **Python 3**, runnable as `python3`. No extra Python packages are needed.
- **Write access** to the remote scanner path, so SporeSync can upload its scanner script.

SporeSync uploads a small Python 3 scanner script to the seedbox and runs it over SSH to list files. Before each scan, it uploads the script again if the remote copy is missing or differs from the copy bundled with SporeSync. If the seedbox is unreachable, the web UI shows a connection status banner.

To check Python on the seedbox:

```sh
ssh <user>@<seedbox> python3 --version
```

## Migrating from seedsync

SporeSync aims for feature parity with seedsync, so the workflow is the same: scan the seedbox, queue files, download, extract, delete. These are the differences to know.

### What changes

- **No LFTP.** Transfers use a native SSH/SFTP engine. LFTP-specific options from seedsync do not carry over. Use the Transfer settings instead.
- **Python 3 on the seedbox.** SporeSync's scanner is a Python 3 script, so the seedbox needs `python3`.
- **New volume layout.** Settings and the database live in `/config`. Downloads live in `/downloads`.
- **New tracking folder.** SporeSync records downloaded and extracted files as marker files in `.sporesync` inside the download directory.

### RAR5 is not supported

SporeSync can extract zip, tar, gz, bz2, xz, 7z, and **RAR4** archives, including multi-part RAR sets. It **does not extract RAR5** archives. RAR5 extraction is out of scope for v1.

If your seedbox has RAR5 archives, extract them yourself after download with a tool such as `unrar` or `7z`, or extract them on the seedbox before SporeSync downloads them.

### What the spec does not cover yet

The spec does not define a way to import seedsync settings, auto-queue patterns, or download history. Plan to:

1. Re-enter your connection, path, and transfer settings in SporeSync.
2. Re-add your auto-queue patterns on the AutoQueue page.

The spec also does not say whether SporeSync reads seedsync's own download records. Until it does, do not rely on files downloaded by seedsync showing as downloaded or extracted.

### Limits to keep in mind

v1 has no login and syncs from one remote host.
