#!/usr/bin/env python3
import json
import os
import sys
from collections import deque
from pathlib import Path


def warn(path: Path, operation: str, error: OSError) -> None:
    print(f"skip {path.as_posix()}: {operation}: {error}", file=sys.stderr)


def entry_type(entry: os.DirEntry) -> str:
    if entry.is_symlink():
        return "symlink"
    if entry.is_dir(follow_symlinks=False):
        return "dir"
    return "file"


def scan(root: Path) -> None:
    pending = deque([Path(".")])
    while pending:
        relative_dir = pending.popleft()
        directory = root / relative_dir
        try:
            with os.scandir(directory) as iterator:
                children = sorted(iterator, key=lambda entry: entry.name)
        except OSError as error:
            warn(relative_dir, "scandir", error)
            continue
        for child in children:
            relative_path = relative_dir / child.name if relative_dir != Path(".") else Path(child.name)
            try:
                stat = child.stat(follow_symlinks=False)
                kind = entry_type(child)
            except OSError as error:
                warn(relative_path, "stat", error)
                continue
            json.dump(
                {
                    "path": relative_path.as_posix(),
                    "size": stat.st_size,
                    "mtime": int(stat.st_mtime),
                    "is_dir": kind == "dir",
                    "type": kind,
                },
                sys.stdout,
                separators=(",", ":"),
            )
            sys.stdout.write("\n")
            if kind == "dir":
                pending.append(relative_path)


def main():
    if len(sys.argv) != 2:
        raise SystemExit("usage: scanner.py <dir>")
    root = Path(sys.argv[1])
    if not root.is_dir():
        raise SystemExit(f"not a directory: {root}")
    scan(root)


if __name__ == "__main__":
    main()
