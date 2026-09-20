#!/usr/bin/env python3
"""Verify built assets, automatic queueing, cancellation and process-crash recovery.

Requires Elide on PATH, Docker, ssh-keygen, and prebuilt frontend assets.
All containers, keys and downloaded content belong to this disposable fixture.
"""
import hashlib
import json
import os
from pathlib import Path
import re
import shutil
import signal
import socket
import subprocess
import tempfile
import time
import urllib.error
import urllib.request

REPOSITORY = Path(__file__).resolve().parent.parent
TIMEOUT_SECONDS = 90
POLL_SECONDS = 0.1
LARGE_FILE_BYTES = 128 * 1024 * 1024
HASH_BUFFER_BYTES = 1024 * 1024
SSH_PORT = 22
POSTGRES_PORT = 5432
POSTGRES_IMAGE = "postgres:17.6-alpine"


def command(*args):
    return subprocess.check_output(args, text=True, stderr=subprocess.DEVNULL).strip()


def wait_for(check):
    deadline = time.monotonic() + TIMEOUT_SECONDS
    while time.monotonic() < deadline:
        try:
            value = check()
            if value:
                return value
        except (urllib.error.URLError, ConnectionError):
            pass
        time.sleep(POLL_SECONDS)
    raise AssertionError("Acceptance check timed out")


def verify(root):
    containers = []
    process = None
    image = f"sporesync-runtime:{root.name}"
    elide = shutil.which("elide")
    if not elide:
        raise RuntimeError("Elide must be on PATH")
    with socket.socket() as listener:
        listener.bind(("127.0.0.1", 0))
        server_port = listener.getsockname()[1]
    base = f"http://127.0.0.1:{server_port}"

    def api(path, method="GET", value=None):
        data = None if value is None else json.dumps(value).encode()
        request = urllib.request.Request(base + path, data=data, method=method,
                                         headers={"Content-Type": "application/json"})
        with urllib.request.urlopen(request, timeout=TIMEOUT_SECONDS) as response:
            return json.load(response)

    def start(environment, log):
        result = subprocess.Popen([elide, "run"], cwd=REPOSITORY / "backend",
                                  env=environment, stdout=log, stderr=log, start_new_session=True)
        try:
            wait_for(lambda: api("/api/status"))
            return result
        except BaseException:
            os.killpg(result.pid, signal.SIGKILL)
            result.wait()
            raise

    def stop(result):
        if result and result.poll() is None:
            os.killpg(result.pid, signal.SIGKILL)
            result.wait()

    try:
        command("ssh-keygen", "-q", "-t", "ed25519", "-N", "", "-f", str(root / "key"))
        shutil.copy(root / "key.pub", root / "authorized_keys")
        shutil.copy(REPOSITORY / "tests/ssh/Dockerfile", root / "Dockerfile")
        command("docker", "build", "-q", "-t", image, str(root))
        ssh = command("docker", "run", "-d", "--rm", "-p", f"127.0.0.1::{SSH_PORT}", image)
        containers.append(ssh)
        database = command("docker", "run", "-d", "--rm", "-p", f"127.0.0.1::{POSTGRES_PORT}",
                           "-e", "POSTGRES_PASSWORD=disposable-fixture", POSTGRES_IMAGE)
        containers.append(database)

        def port(container, number):
            return int(command("docker", "port", container, f"{number}/tcp").split(":")[-1])

        ssh_port = port(ssh, SSH_PORT)
        host_key = command("docker", "exec", ssh, "cat", "/etc/ssh/ssh_host_ed25519_key.pub").split()
        (root / "known_hosts").write_text(f"[127.0.0.1]:{ssh_port} {' '.join(host_key[:2])}\n")
        downloads = root / "downloads"
        downloads.mkdir()
        environment = dict(os.environ, SERVER_PORT=str(server_port),
                           SPRING_DATASOURCE_URL=f"jdbc:postgresql://127.0.0.1:{port(database, POSTGRES_PORT)}/postgres",
                           SPRING_DATASOURCE_USERNAME="postgres", SPRING_DATASOURCE_PASSWORD="disposable-fixture",
                           SPORESYNC_SSH_PRIVATE_KEY=str(root / "key"),
                           SPORESYNC_SSH_KNOWN_HOSTS=str(root / "known_hosts"))
        with (root / "backend.log").open("w") as log:
            process = start(environment, log)
            with urllib.request.urlopen(base) as response:
                html = response.read().decode()
            asset = re.search(r'src="([^"]+\.js)"', html)
            assert asset, "Built frontend was not served"
            with urllib.request.urlopen(base + asset.group(1)) as response:
                assert response.read(), "Frontend JavaScript was empty"
            settings = dict(host="127.0.0.1", port=ssh_port, username="scanner", source="/seed",
                            destination=str(downloads), scanSeconds=10, automatic=True, temporaryFiles=True)
            assert api("/api/settings", "PUT", settings) == settings
            api("/api/inventory/scan", "POST")
            api("/api/inventory/scan", "POST")
            wait_for(lambda: any(job["state"] == "COMPLETE" for job in api("/api/downloads")))
            assert (downloads / "nested/日本語 file.txt").read_text() == "sample\n"
            print("PASS: production assets, settings, automatic discovery and SFTP content", flush=True)
            settings["automatic"] = False
            api("/api/settings", "PUT", settings)
            command("docker", "exec", ssh, "truncate", "-s", str(LARGE_FILE_BYTES), "/seed/large.bin")
            job = api("/api/downloads", "POST", {"path": "large.bin"})

            def current():
                return next(item for item in api("/api/downloads") if item["id"] == job["id"])

            wait_for(lambda: current()["state"] == "RUNNING" and current()["bytesDone"] > 0)
            api(f"/api/downloads/{job['id']}/cancel", "POST")
            wait_for(lambda: current()["state"] == "CANCELLED")
            assert not (downloads / "large.bin").exists()
            api(f"/api/downloads/{job['id']}/retry", "POST")
            wait_for(lambda: current()["state"] == "RUNNING")
            stop(process)
            process = start(environment, log)
            wait_for(lambda: current()["state"] == "COMPLETE")
            assert current()["attempts"] == 2
            with (downloads / "large.bin").open("rb") as content:
                hasher = hashlib.sha256()
                for block in iter(lambda: content.read(HASH_BUFFER_BYTES), b""):
                    hasher.update(block)
                digest = hasher.hexdigest()
            expected = command("docker", "exec", ssh, "sha256sum", "/seed/large.bin").split()[0]
            assert digest == expected
            assert current()["bytesDone"] == LARGE_FILE_BYTES
            print("PASS: cancellation, retry, process kill/restart, durable progress and exact resumed content", flush=True)
    except BaseException:
        if (root / "backend.log").exists():
            print((root / "backend.log").read_text()[-12000:])
        raise
    finally:
        stop(process)
        for container in containers:
            subprocess.run(["docker", "rm", "-f", container], capture_output=True, check=False)
        subprocess.run(["docker", "image", "rm", image], capture_output=True, check=False)


if __name__ == "__main__":
    with tempfile.TemporaryDirectory(prefix="sporesync-runtime-") as directory:
        verify(Path(directory))
