#!/usr/bin/env python3
import json
import os
import sys
from pathlib import Path


def scan(root: Path):
    entries = []
    pending = [Path(".")]
    while pending:
        relative_dir = pending.pop(0)
        directory = root / relative_dir
        children = sorted(os.scandir(directory), key=lambda entry: entry.name)
        for child in children:
            relative_path = relative_dir / child.name if relative_dir != Path(".") else Path(child.name)
            stat = child.stat(follow_symlinks=False)
            is_dir = child.is_dir(follow_symlinks=False)
            entries.append(
                {
                    "path": relative_path.as_posix(),
                    "size": stat.st_size,
                    "mtime": int(stat.st_mtime),
                    "is_dir": is_dir,
                }
            )
            if is_dir:
                pending.append(relative_path)
    return entries


def main():
    if len(sys.argv) != 2:
        raise SystemExit("usage: scanner.py <dir>")
    root = Path(sys.argv[1])
    if not root.is_dir():
        raise SystemExit(f"not a directory: {root}")
    json.dump(scan(root), sys.stdout, separators=(",", ":"))
    sys.stdout.write("\n")


if __name__ == "__main__":
    main()
