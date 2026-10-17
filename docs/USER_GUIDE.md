# SporeSync User Guide

SporeSync mirrors files from a remote seedbox to your machine over SSH, with a web UI for browsing, queuing, extracting, and deleting files. It is a rewrite of [seedsync](https://github.com/nitrobass24/seedsync).

This guide is based on the [SporeSync specification](SPEC.md). SporeSync is still being built, so parts of this guide describe planned behavior.

## How to read this guide

- **Specified** means the behavior is decided in the spec.
- **Not yet established** means the spec leaves the detail to the implementation, such as the image name, web port, settings file format, or exact environment variable names. These are shown as `<placeholders>`. This guide will be updated when the implementation decides them.

## Before you start

- **No login in v1.** SporeSync has no authentication and no multi-user support. Run it only on a trusted network, and do not expose its web UI to the internet.
- **One remote host.** v1 syncs from a single seedbox.
- **Python 3 on the remote host.** See [Remote host requirements](#remote-host-requirements).
- **Docker** on the local machine. Docker is the primary way to run SporeSync.

## Docker setup

SporeSync ships as a single Docker image. The image runs on Elide, the only runtime it uses. You do not need Java, Node.js, or anything else installed locally.

### Volumes

| Container path | Holds | Notes |
|----------------|-------|-------|
| `/config` | Settings file and the H2 database | Back this up. It keeps your settings, auto-queue patterns, and transfer history. |
| `/downloads` | Your local download directory | Files from the seedbox land here. SporeSync also keeps its tracking markers here, in `/downloads/.sporesync`. |

Mount both volumes to folders on the host so your data survives container upgrades.

### Example

The image name and web port are **not yet established**. Replace the placeholders once they are published.

```sh
docker run -d \
  --name sporesync \
  -p <host-port>:<container-port> \
  -v /path/on/host/sporesync-config:/config \
  -v /path/on/host/downloads:/downloads \
  <image>
```

The same setup with Docker Compose:

```yaml
services:
  sporesync:
    image: <image>
    ports:
      - "<host-port>:<container-port>"
    volumes:
      - /path/on/host/sporesync-config:/config
      - /path/on/host/downloads:/downloads
    restart: unless-stopped
```

Then open the web UI in a browser and fill in the Settings page.

### SSH keys in Docker

If you log in to the seedbox with an SSH key, the key path setting must be a path **inside the container**. A host path such as `~/.ssh/id_ed25519` does not exist there.

The spec does not define a key location. The simplest option is to keep the key in your config folder, which is already mounted:

1. Copy the private key to `/path/on/host/sporesync-config/ssh/id_ed25519`.
2. Set the key path to `/config/ssh/id_ed25519`.

Or mount the key read-only on its own and use that container path:

```sh
-v ~/.ssh/id_ed25519:/keys/id_ed25519:ro
```

### Health check

The container exposes a health endpoint at `/actuator/health`. You can point a Docker or Compose health check at it.

## Settings

You can change settings in three ways:

1. The **Settings** page in the web UI (`/settings`). Each field saves on its own and shows validation errors inline. Some changes show a "restart required" notice.
2. The **settings file** in `/config`. It is named `settings.toml` or `settings.yaml`; the format is **not yet established** (the spec leans toward TOML).
3. **Environment variables**, which override values from the settings file. See [Environment overrides](#environment-overrides).

SporeSync validates settings when it loads them and when you save them.

### Settings reference

The groups and settings below are specified. Their exact key names, types, and defaults are **not yet established**.

| Group | Setting | What it does |
|-------|---------|--------------|
| General | Debug | Turns on debug logging. Logs are structured JSON. |
| General | Verbose | Turns on verbose output. |
| Connection | Host | Seedbox hostname or IP address. |
| Connection | Port | SSH port on the seedbox. |
| Connection | Username | SSH username. |
| Connection | Auth method | Whether to log in with an SSH key or a password. |
| Connection | Key path or password | Path to the private key (for key auth) or the password. In Docker, use a container path; see [SSH keys in Docker](#ssh-keys-in-docker). |
| Paths | Remote dir | Directory on the seedbox to mirror. |
| Paths | Local dir | Local download directory. In Docker, this is `/downloads`. |
| Paths | Remote scanner path | Where SporeSync places its scanner script on the seedbox. |
| Transfer | Concurrent file limit | How many files download at the same time. |
| Transfer | Per-file connection limit | How many SFTP connections one file may use. Each file can be split into byte ranges that download in parallel. |
| Transfer | Bandwidth limit | Overall download speed cap. |
| Scan | Remote interval | How often SporeSync scans the seedbox. |
| Scan | Local interval | How often SporeSync scans the local download directory. |
| Extraction | Enabled | Extract archives automatically after download. |
| Extraction | Delete archive after extract | Remove the archive once extraction succeeds. |
| AutoQueue | Enabled | Queue new remote files automatically. |
| AutoQueue | Patterns-only mode | When on, only files matching a pattern are queued. When off, every new file is queued. |

Auto-queue patterns are not part of the settings file. You manage them on the AutoQueue page (`/autoqueue`), and they are stored in the H2 database in `/config`.

### Environment overrides

Environment variables override settings file values using Spring Boot's [relaxed binding](https://docs.spring.io/spring-boot/reference/features/external-config.html#features.external-config.typesafe-configuration-properties.relaxed-binding.environment-variables).

The exact variable names are **not yet established**. They will be generated from the settings property names using Spring's rules, which are:

1. Replace dots (`.`) with underscores (`_`).
2. Remove dashes (`-`).
3. Convert to uppercase.

So a property written as `<prefix>.<group>.<setting-name>` becomes `<PREFIX>_<GROUP>_<SETTINGNAME>`. Do not guess names until the implementation publishes the property list.

In Compose, pass overrides under `environment:`:

```yaml
services:
  sporesync:
    environment:
      <GENERATED_VARIABLE_NAME>: "<value>"
```

## Remote host requirements

SporeSync needs very little on the seedbox:

- **SSH access** with SFTP enabled, using a key or a password.
- **Python 3**, runnable as `python3`. No extra Python packages are needed.
- **Write access** to the remote scanner path, so SporeSync can upload its scanner script.

SporeSync uploads a small Python 3 scanner script to the seedbox, runs it over SSH to list files, and re-uploads it whenever its own copy changes. If the seedbox is unreachable, the web UI shows a connection status banner.

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
