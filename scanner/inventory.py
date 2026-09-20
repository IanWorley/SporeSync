"""Emit a complete inventory on stdout, or a diagnostic on stderr."""
import argparse
import json
import os
from pathlib import Path
import stat
import sys
from typing import List, Literal, TypedDict

SCHEMA_VERSION = 1
SCAN_FAILED = 1


EntryType = Literal["file", "directory", "symlink", "other"]


class Entry(TypedDict):
    path: str
    type: EntryType
    sizeBytes: int
    modifiedTimeNs: int


class Inventory(TypedDict):
    schemaVersion: int
    entries: List[Entry]


def scan(source: Path) -> Inventory:
    entries: List[Entry] = []

    def visit(directory: Path) -> None:
        with os.scandir(directory) as children:
            for child in sorted(children, key=lambda item: item.name):
                metadata = child.stat(follow_symlinks=False)
                mode = metadata.st_mode
                kind: EntryType
                if stat.S_ISLNK(mode):
                    kind = "symlink"
                elif stat.S_ISDIR(mode):
                    kind = "directory"
                elif stat.S_ISREG(mode):
                    kind = "file"
                else:
                    kind = "other"
                path = Path(child.path)
                entries.append({
                    "path": path.relative_to(source).as_posix(),
                    "type": kind,
                    "sizeBytes": metadata.st_size,
                    "modifiedTimeNs": metadata.st_mtime_ns,
                })
                # Report links without traversing outside the configured source.
                if kind == "directory":
                    visit(path)

    visit(source)
    return {"schemaVersion": SCHEMA_VERSION, "entries": entries}


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("source", type=Path)
    args = parser.parse_args()
    try:
        inventory = scan(args.source)
    except OSError as error:
        print(f"Scan failed: {error}", file=sys.stderr)
        return SCAN_FAILED
    # ASCII escaping also preserves filenames containing undecodable POSIX bytes.
    print(json.dumps(inventory, ensure_ascii=True))
    return 0


if __name__ == "__main__":
    sys.exit(main())
