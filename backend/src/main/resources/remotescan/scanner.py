#!/usr/bin/env python3
import json
import os
import re
import sys

LFTP_STATUS_SUFFIX = ".lftp-pget-status"


def lftp_status_size(status):
    lines = [line.strip() for line in status.splitlines() if line.strip()]
    total = re.match(r"^size=(\d+)$", lines[0]) if lines else None
    segments = lines[1:]
    if not total or len(segments) % 2:
        return 0
    empty = 0
    for pos_line, limit_line in zip(segments[::2], segments[1::2]):
        pos = re.match(r"^\d+\.pos=(\d+)$", pos_line)
        limit = re.match(r"^\d+\.limit=(\d+)$", limit_line)
        if not pos or not limit:
            return 0
        empty += int(limit.group(1)) - int(pos.group(1))
    return int(total.group(1)) - empty


def scan_dir(path, prefix):
    subtrees = []
    for entry in os.scandir(path):
        if entry.name.endswith(LFTP_STATUS_SUFFIX):
            continue
        try:
            subtrees.append(scan_entry(entry, prefix))
        except FileNotFoundError:
            continue
    return sorted(subtrees, key=lambda subtree: subtree[0]["path"])


def scan_entry(entry, prefix):
    path = prefix + entry.name.encode("utf-8", "surrogateescape").decode("utf-8", "replace")
    stat = entry.stat()
    if entry.is_dir():
        subtrees = scan_dir(entry.path, path + "/")
        size = sum(subtree[0]["size"] for subtree in subtrees)
        return [describe(path, size, stat, True)] + [item for subtree in subtrees for item in subtree]
    size = stat.st_size
    status_path = entry.path + LFTP_STATUS_SUFFIX
    if os.path.isfile(status_path):
        with open(status_path) as status:
            size = lftp_status_size(status.read())
    return [describe(path, size, stat, False)]


def describe(path, size, stat, is_dir):
    return {"path": path, "size": size, "mtime": stat.st_mtime, "is_dir": is_dir}


def main():
    if len(sys.argv) != 2:
        sys.exit("usage: scanner.py <dir>")
    root = sys.argv[1]
    if not os.path.exists(root):
        sys.exit("SystemScannerError: Path does not exist: {}".format(root))
    if not os.path.isdir(root):
        sys.exit("SystemScannerError: Path is not a directory: {}".format(root))
    entries = [item for subtree in scan_dir(root, "") for item in subtree]
    json.dump(entries, sys.stdout, separators=(",", ":"))


if __name__ == "__main__":
    main()
