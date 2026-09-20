# SporeSync

SporeSync is being rebuilt around the [starting brief](docs/plan.md).
The first implemented component is a dependency-free Python remote scanner.
There is no backend or dashboard yet.

## Run the scanner

Requires Python 3.9 or newer on the scanning machine:

```bash
python3 scanner/inventory.py /absolute/source/directory
python3 -m unittest discover -s tests -v
```

Successful scans write one JSON object to stdout with `schemaVersion: 1` and an
`entries` array. Each entry has a source-relative POSIX `path`, `type` (`file`,
`directory`, `symlink`, or `other`), `sizeBytes`, and `modifiedTimeNs` (integer
nanoseconds since the Unix epoch). Only regular-file sizes describe downloadable
content. The root itself is omitted; empty child directories are included.
Links are reported without traversal. Consumers must only queue regular files.

A filesystem error exits nonzero, writes a diagnostic to stderr, and emits no
JSON. A scan is not a filesystem snapshot: files can change during traversal.
The scanner holds the inventory in memory before publishing it. Transfer-time
validation and protection against concurrent directory replacement belong to
later work; this scanner is intended for a trusted seedbox directory.

## Verify real SSH upload and execution

Requires a running Docker engine, OpenSSH client, and `ssh-keygen`:

```bash
SPORESYNC_SSH_TEST=1 python3 -m unittest discover -s tests -v
```

The suite builds a disposable Debian SSH/Python container, generates a temporary
client key, binds an ephemeral loopback port, pins the container's SSH host key,
and uploads the scanner over SSH. It verifies nested Unicode filenames and a
permission-denied source. Containers, images, and temporary keys are cleaned up
by the suite. The Debian base follows bookworm updates; it is a test fixture,
not a production deployment image.
