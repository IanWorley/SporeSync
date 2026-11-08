#!/usr/bin/env python3
"""Validate declared authorship without inferring identity from GitHub accounts."""

from __future__ import annotations

import argparse
import hashlib
from html.parser import HTMLParser
import json
import os
from pathlib import Path
import re
import subprocess
import sys

REPOSITORY = "IanWorley/SporeSync"
ROOT = Path(__file__).resolve().parents[1]
PREFIX = "SporeSync-"
GITHUB_TIMEOUT = 60
MAX_METADATA_LENGTH = 100
MAX_USERNAME_LENGTH = 39
AGENT = "agent (not a person)"
FIELDS = ("Authorship", "Harness", "Effort", "Model", "On-Behalf-Of")
EFFORTS = {"low", "medium", "high", "xhigh", "max", "ultra"}
PLACEHOLDERS = {"unknown", "todo", "tbd", "none", "n/a", "placeholder", "harness", "model", "effort", "your-name", "@username", "@your-name", "@githubusername", "your-model", "your-harness", "your model", "your harness"}
TEXT_VALUE = re.compile(rf"[A-Za-z0-9][A-Za-z0-9 ./+_()-]{{0,{MAX_METADATA_LENGTH - 1}}}")
USERNAME = re.compile(rf"@[A-Za-z0-9](?:[A-Za-z0-9-]{{0,{MAX_USERNAME_LENGTH - 2}}}[A-Za-z0-9])?")
SHA = re.compile(r"[0-9a-f]{40}")
PRE_POLICY_COMMITS = {
    "9b377cd7b53d115ff97b1974afbdad283c7d5a3f",
    "20cafe87ffdbe6c17a01ed76f60579a58bca7d74",
}
DISCLOSURE = """SporeSync-Authorship: agent (not a person)
SporeSync-Harness: T3 Code / Codex
SporeSync-Effort: high
SporeSync-Model: gpt-6.1-sol
SporeSync-On-Behalf-Of: @IanWorley"""


class DisclosureText(HTMLParser):
    VOID_TAGS = {"area", "base", "br", "col", "embed", "hr", "img", "input", "link", "meta", "param", "source", "track", "wbr"}

    def __init__(self) -> None:
        super().__init__(convert_charrefs=False)
        self.tags: list[str] = []
        self.parts: list[str] = []

    def handle_starttag(self, tag: str, attrs: list[tuple[str, str | None]]) -> None:
        if not self.tags:
            self.parts.append("<markup>")
        if tag not in self.VOID_TAGS:
            self.tags.append(tag)

    def handle_endtag(self, tag: str) -> None:
        if tag in self.tags:
            index = len(self.tags) - 1 - self.tags[::-1].index(tag)
            del self.tags[index:]
        if not self.tags:
            self.parts.append("</markup>")

    def handle_data(self, data: str) -> None:
        if not self.tags:
            self.parts.append(data)

    def handle_entityref(self, name: str) -> None:
        self.handle_data(f"&{name};")

    def handle_charref(self, name: str) -> None:
        self.handle_data(f"&#{name};")


def validate(text: str) -> list[str]:
    visible = DisclosureText()
    visible.feed(text)
    visible.close()
    values: dict[str, str] = {}
    errors = []
    for line in "".join(visible.parts).splitlines():
        match = re.fullmatch(r"SporeSync-([A-Za-z-]+):[ \t]*(.*)", line)
        if not match:
            continue
        field, value = match.groups()
        if field not in FIELDS:
            errors.append(f"unknown field {PREFIX}{field}")
        if field in values:
            errors.append(f"duplicate field {PREFIX}{field}")
        values[field] = value.strip()
    authorship = values.get("Authorship", "")
    if not authorship:
        errors.append("missing field SporeSync-Authorship")
        return errors
    if authorship == "human":
        if any(field in values for field in FIELDS if field != "Authorship"):
            errors.append("human declaration must not include agent metadata")
        return errors
    if authorship != AGENT:
        errors.append("malformed field SporeSync-Authorship; expected 'agent (not a person)' or 'human'")
    for field in FIELDS[1:]:
        value = values.get(field, "")
        if not value:
            errors.append(f"missing field {PREFIX}{field}")
        elif value.lower() in PLACEHOLDERS or not TEXT_VALUE.fullmatch(value.removeprefix("@") if field == "On-Behalf-Of" else value):
            errors.append(f"malformed field {PREFIX}{field}; empty, placeholder, or unsafe value")
        elif field == "Effort" and value not in EFFORTS:
            errors.append(f"malformed field {PREFIX}{field}; unsupported effort")
        elif field == "On-Behalf-Of" and not USERNAME.fullmatch(value):
            errors.append(f"malformed field {PREFIX}{field}; expected @GitHubUsername")
    return errors


def git(*args: str) -> str:
    return subprocess.check_output(("git", *args), cwd=ROOT, text=True, errors="replace", stderr=subprocess.PIPE).strip()


def validate_commits(base: str, head: str) -> list[str]:
    errors = []
    commits = git("rev-list", "--reverse", f"{base}..{head}").splitlines()
    for sha in commits:
        if sha in PRE_POLICY_COMMITS:
            print(f"EXEMPT pre-policy commit {sha}; bounded compatibility baseline, not a disclosure pass")
            continue
        problems = validate(git("show", "-s", "--format=%B", sha))
        errors.extend(f"commit {sha}: {problem}" for problem in problems)
        if not problems:
            print(f"PASS commit {sha}: declared authorship is valid")
    print(f"Inspected {len(commits)} new commits, including any explicitly exempt pre-policy commits.")
    return errors


def gh_pr(pr: int | None) -> dict:
    selector = str(pr) if pr is not None else git("branch", "--show-current")
    if not selector:
        raise ValueError("Detached HEAD requires --pr or --pr-body")
    command = ["gh", "pr", "view", selector]
    command += ["--repo", REPOSITORY, "--json", "body,url,headRefOid"]
    response = subprocess.run(command, cwd=ROOT, capture_output=True, text=True, timeout=GITHUB_TIMEOUT)
    if response.returncode:
        raise ValueError("PR body unavailable; pass --pr-body PATH for a new PR, or use existing gh authentication")
    data = json.loads(response.stdout)
    if not data["url"].startswith(f"https://github.com/{REPOSITORY}/pull/"):
        raise ValueError("Unexpected PR repository")
    return data


def check_event(event: dict, event_name: str) -> list[str]:
    if event["repository"]["full_name"] != REPOSITORY:
        raise ValueError("Event repository must be IanWorley/SporeSync")
    if event_name == "pull_request":
        pull = event["pull_request"]
        number, base, head = pull["number"], pull["base"]["sha"], pull["head"]["sha"]
        if type(number) is not int or number <= 0 or not SHA.fullmatch(base) or not SHA.fullmatch(head):
            raise ValueError("Invalid PR commit range")
        subprocess.run(("git", "fetch", "--no-tags", "origin", f"refs/pull/{number}/head"), cwd=ROOT, check=True, capture_output=True)
        if git("rev-parse", "FETCH_HEAD") != head:
            raise ValueError("PR head changed during event verification; rerun the current event")
        return [*(f"PR body: {error}" for error in validate(pull.get("body") or "")), *validate_commits(base, head)]
    if event_name == "issues":
        label, body = "issue body", event["issue"].get("body")
    elif event_name in {"issue_comment", "pull_request_review_comment"}:
        label, body = "comment", event["comment"].get("body")
    elif event_name == "pull_request_review":
        label, body = "review", event["review"].get("body")
        if not body:
            print("NOT APPLICABLE: review has no authored text")
            return []
    else:
        raise ValueError("Unsupported disclosure event")
    return [f"{label}: {error}" for error in validate(body or "")]


def main() -> int:
    cli = argparse.ArgumentParser(description=__doc__)
    cli.add_argument("mode", choices=("local", "event"))
    cli.add_argument("--pr-body", type=Path)
    cli.add_argument("--pr", type=int)
    cli.add_argument("--base", default="origin/main")
    cli.add_argument("--event-file", type=Path)
    cli.add_argument("--event-name", default=os.environ.get("GITHUB_EVENT_NAME"))
    args = cli.parse_args()
    try:
        if args.mode == "event":
            event_file = args.event_file or Path(os.environ["GITHUB_EVENT_PATH"])
            errors = check_event(json.loads(event_file.read_text()), args.event_name)
        else:
            if args.pr is not None and args.pr <= 0:
                raise ValueError("PR number must be positive")
            body = args.pr_body.read_text() if args.pr_body else gh_pr(args.pr)["body"]
            print("PR body SHA256:", hashlib.sha256((body or "").encode()).hexdigest())
            base = git("merge-base", args.base, "HEAD")
            errors = [*(f"PR body: {error}" for error in validate(body or "")), *validate_commits(base, "HEAD")]
        for error in errors:
            print("FAIL", error)
        print("Authorship disclosure failed." if errors else "Authorship declarations passed; declared identity is not independently verified.")
        return 1 if errors else 0
    except (OSError, ValueError, KeyError, TypeError, subprocess.SubprocessError) as error:
        print(f"FAIL disclosure check unavailable: {type(error).__name__}; {error if isinstance(error, ValueError) else 'check prerequisites and event data'}", file=sys.stderr)
        return 1


if __name__ == "__main__":
    sys.exit(main())
