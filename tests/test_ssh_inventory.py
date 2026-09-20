"""Opt-in real SSH test: SPORESYNC_SSH_TEST=1 python3 -m unittest discover -s tests."""
import json
import os
from pathlib import Path
import shutil
import subprocess
import tempfile
import time
import unittest
import uuid

ROOT = Path(__file__).resolve().parents[1]
SSH_READY_TIMEOUT_SECONDS = 30
SSH_RETRY_INTERVAL_SECONDS = 0.2
SSH_CONNECT_TIMEOUT_SECONDS = 2
SAMPLE_BYTES = 7


@unittest.skipUnless(os.environ.get("SPORESYNC_SSH_TEST") == "1", "requires Docker and SSH")
class SshInventoryTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        temporary = tempfile.TemporaryDirectory()
        cls.addClassCleanup(temporary.cleanup)
        folder = Path(temporary.name)
        key = folder / "key"
        subprocess.run(["ssh-keygen", "-q", "-t", "ed25519", "-N", "", "-f", str(key)], check=True)
        shutil.copy(ROOT / "tests/ssh/Dockerfile", folder)
        shutil.copy(key.with_suffix(".pub"), folder / "authorized_keys")
        (folder / ".dockerignore").write_text("*\n!Dockerfile\n!authorized_keys\n")
        tag = f"sporesync-scanner-test:{uuid.uuid4().hex}"
        subprocess.run(["docker", "build", "-q", "-t", tag, str(folder)], check=True)
        cls.addClassCleanup(subprocess.run, ["docker", "image", "rm", tag], capture_output=True)
        container = subprocess.check_output([
            "docker", "run", "-d", "--rm", "-p", "127.0.0.1::22", tag,
        ], text=True).strip()
        cls.addClassCleanup(subprocess.run, ["docker", "stop", container], capture_output=True)
        address = subprocess.check_output(["docker", "port", container, "22"], text=True).strip()
        port = address.rsplit(":", 1)[1]
        # Trust the key obtained through Docker, not an unauthenticated network scan.
        host_key = subprocess.check_output([
            "docker", "exec", container, "cat", "/etc/ssh/ssh_host_ed25519_key.pub",
        ], text=True).split()
        known_hosts = folder / "known_hosts"
        known_hosts.write_text(f"[127.0.0.1]:{port} {' '.join(host_key[:2])}\n")
        cls.ssh = [
            "ssh", "-p", port, "-i", str(key), "-o", "BatchMode=yes",
            "-o", "IdentitiesOnly=yes", "-o", "StrictHostKeyChecking=yes",
            "-o", f"UserKnownHostsFile={known_hosts}",
            "-o", f"ConnectTimeout={SSH_CONNECT_TIMEOUT_SECONDS}", "scanner@127.0.0.1",
        ]
        deadline = time.monotonic() + SSH_READY_TIMEOUT_SECONDS
        while True:
            result = subprocess.run([*cls.ssh, "true"], capture_output=True, text=True)
            if result.returncode == 0:
                break
            if time.monotonic() >= deadline:
                raise RuntimeError(f"SSH did not become ready: {result.stderr}")
            time.sleep(SSH_RETRY_INTERVAL_SECONDS)
        subprocess.run(
            [*cls.ssh, "cat > /home/scanner/inventory.py"],
            input=(ROOT / "scanner/inventory.py").read_bytes(), check=True,
        )

    def remote_scan(self, source):
        return subprocess.run(
            [*self.ssh, f"python3 /home/scanner/inventory.py {source}"],
            capture_output=True, text=True, check=False,
        )

    def test_uploaded_scanner_returns_nested_inventory(self):
        result = self.remote_scan("/seed")
        self.assertEqual(result.returncode, 0, result.stderr)
        entries = {entry["path"]: entry for entry in json.loads(result.stdout)["entries"]}
        self.assertEqual(entries["empty"]["type"], "directory")
        self.assertEqual(entries["nested/日本語 file.txt"]["sizeBytes"], SAMPLE_BYTES)

    def test_inaccessible_source_returns_error_without_inventory(self):
        result = self.remote_scan("/restricted")
        self.assertNotEqual(result.returncode, 0)
        self.assertEqual(result.stdout, "")
        self.assertIn("Permission denied", result.stderr)
