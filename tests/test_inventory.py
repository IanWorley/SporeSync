import json
import os
from pathlib import Path
import subprocess
import sys
import tempfile
import unittest

from scanner.inventory import scan

SCANNER = Path(__file__).resolve().parents[1] / "scanner" / "inventory.py"
FIXED_TIME_NS = 1_700_000_000_123_456_789


class InventoryTests(unittest.TestCase):
    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory()
        self.addCleanup(self.temporary.cleanup)
        self.source = Path(self.temporary.name)

    def run_scanner(self, source):
        return subprocess.run(
            [sys.executable, str(SCANNER), str(source)],
            capture_output=True, text=True, check=False,
        )

    def test_nested_unicode_filename_preserves_metadata(self):
        directory = self.source / "nested folder"
        directory.mkdir()
        file = directory / "日本語 file.txt"
        content = "hello 🌍".encode()
        file.write_bytes(content)
        os.utime(file, ns=(FIXED_TIME_NS, FIXED_TIME_NS))
        result = self.run_scanner(self.source)
        self.assertEqual(result.returncode, 0, result.stderr)
        inventory = json.loads(result.stdout)
        self.assertEqual(inventory["schemaVersion"], 1)
        self.assertEqual(inventory["entries"][1], {
            "path": "nested folder/日本語 file.txt", "type": "file",
            "sizeBytes": len(content), "modifiedTimeNs": file.stat().st_mtime_ns,
        })

    def test_empty_source_has_no_entries(self):
        self.assertEqual(scan(self.source)["entries"], [])

    def test_empty_directory_is_reported(self):
        (self.source / "empty").mkdir()
        entry, = scan(self.source)["entries"]
        self.assertEqual((entry["path"], entry["type"]), ("empty", "directory"))

    def test_symlinks_are_reported_without_following_them(self):
        (self.source / "loop").symlink_to(self.source, target_is_directory=True)
        (self.source / "broken").symlink_to(self.source / "missing")
        entries = scan(self.source)["entries"]
        self.assertEqual([entry["path"] for entry in entries], ["broken", "loop"])
        self.assertTrue(all(entry["type"] == "symlink" for entry in entries))

    def test_missing_source_fails_without_partial_json(self):
        result = self.run_scanner(self.source / "missing")
        self.assertNotEqual(result.returncode, 0)
        self.assertEqual(result.stdout, "")
        self.assertIn("Scan failed:", result.stderr)

    def test_regular_file_is_not_a_source_directory(self):
        file = self.source / "file"
        file.touch()
        self.assertNotEqual(self.run_scanner(file).returncode, 0)

    def test_newline_and_quote_in_filename_round_trip(self):
        name = 'line\nbreak".txt'
        (self.source / name).touch()
        result = self.run_scanner(self.source)
        self.assertEqual(json.loads(result.stdout)["entries"][0]["path"], name)


if __name__ == "__main__":
    unittest.main()
