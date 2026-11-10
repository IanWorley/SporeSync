#!/usr/bin/env python3
"""Run SporeSync's local CI gates and retain evidence. Requires Python 3.11+."""

from __future__ import annotations

import argparse
from dataclasses import asdict, dataclass
from datetime import datetime, timezone
import hashlib
import html
import json
import math
import os
from pathlib import Path
import re
import shlex
import signal
import subprocess
import sys
import time
from typing import Sequence

REPOSITORY = "IanWorley/SporeSync"
ROOT = Path(__file__).resolve().parents[1]
DEFAULT_TIMEOUT = 900.0
GH_TIMEOUT = 60
FAILURE_TAIL_LINES = 20
GH_PAGE_SIZE = 100
MIN_SECRET_LENGTH = 4
TERMINATION_TIMEOUT = 2.0
MIN_PYTHON_VERSION = (3, 11)
MAX_USERNAME_LENGTH = 39
USERNAME_PATTERN = rf"[A-Za-z0-9](?:[A-Za-z0-9-]{{0,{MAX_USERNAME_LENGTH - 2}}}[A-Za-z0-9])?"
WORKFLOW_HASHES = {'ci.yml': 'df0d35855dbfaea293d3190cb5e90730091bb18be49400faeb640c0689f86b8a'}
EXCLUSIONS = (
    "GitHub runner provisioning, checkout, caches, and toolchain downloads are hosted setup. "
    "Install Python 3.11+, pinned Elide (.elideversion), and Vite+ with bun locally. "
    "Dependency installation runs below. CodeRabbit and Cursor Security Agent require hosted services "
    "and are excluded, never counted as passing. No app smoke job exists in these workflows."
)


@dataclass(frozen=True)
class Check:
    name: str
    command: tuple[str, ...]
    directory: str
    ci: str


CHECKS = (
    Check("python-version", ("python3", "--version"), ".", "ci.yml / verifier / local Python prerequisite"),
    Check("elide-version", ("elide", "--version"), ".", "ci.yml / setup-elide / .elideversion pin"),
    Check("vp-version", ("vp", "--version"), "frontend", "ci.yml / setup-vp / local Vite+ prerequisite"),
    Check("bun-version", ("bun", "--version"), "frontend", "ci.yml / setup-vp / frontend/package.json packageManager pin"),
    Check("runner-tests", ("python3", "-m", "unittest", "discover", "-s", "scripts", "-p", "test_*.py", "-v"), ".", "ci.yml / verifier"),
    Check("frontend-install", ("vp", "install"), "frontend", "ci.yml / setup-vp dependency installation for frontend-* and backend-test"),
    Check("backend-install", ("elide", "install"), ".", "ci.yml / backend-* / elide install"),
    Check("frontend-build", ("vp", "build"), "frontend", "ci.yml / backend-test / vp build"),
    Check("backend-build", ("elide", "build"), ".", "ci.yml / backend-test / elide build"),
    Check("backend-test", ("elide", "test"), ".", "ci.yml / backend-test / elide test"),
    Check("backend-format", ("elide", "run", "format-check"), "backend", "ci.yml / backend-format / ktlint_official, backend/.editorconfig"),
    Check("backend-lint", ("elide", "run", "lint"), "backend", "ci.yml / backend-lint / detekt defaults including MagicNumber"),
    Check("frontend-test", ("vp", "test", "run"), "frontend", "ci.yml / frontend-test / Vitest"),
    Check("frontend-format", ("vp", "fmt", "--check"), "frontend", "ci.yml / frontend-format / Oxfmt, frontend/vite.config.ts"),
    Check("frontend-lint", ("vp", "lint"), "frontend", "ci.yml / frontend-lint / Oxlint rules and full TypeScript check, frontend/vite.config.ts"),
)


@dataclass(frozen=True)
class Result:
    name: str
    command: str
    directory: str
    ci: str
    status: str
    seconds: float
    detail: str
    log: str


def redact(value: str) -> str:
    value = re.sub(r"\x1b\[[0-?]*[ -/]*[@-~]", "", value)
    for key, secret in os.environ.items():
        if re.search(r"TOKEN|SECRET|PASSWORD|CREDENTIAL|API_KEY", key, re.I) and len(secret) >= MIN_SECRET_LENGTH:
            value = value.replace(secret, "[REDACTED]")
    value = re.sub(r"(?:gh[pousr]_[A-Za-z0-9_]+|github_pat_[A-Za-z0-9_]+)", "[REDACTED]", value)
    value = re.sub(r"(?i)(authorization\s*[:=]\s*)([^\r\n]+)", r"\1[REDACTED]", value)
    value = re.sub(r"(https?://)[^\s/@]+:[^\s/@]+@", r"\1[REDACTED]@", value)
    value = re.sub(r"(?i)((?:token|password|secret|api[_-]?key)\s*[:=]\s*)[^\s,;]+", r"\1[REDACTED]", value)
    return value


def escaped(value: str) -> str:
    return html.escape(redact(value), quote=True).replace("|", "&#124;").replace("`", "&#96;").replace("\n", "<br>")


def metadata(value: str) -> str:
    value = redact(re.sub(r"[\x00-\x1f\x7f]+", " ", value))
    encoded = "".join(f"&#{ord(character)};" if character in "\\*_[]()!@#|`" else html.escape(character, quote=True) for character in value)
    return f"<code>{encoded}</code>"


def git(root: Path, *args: str) -> str:
    return subprocess.check_output(("git", *args), cwd=root, text=True, stderr=subprocess.PIPE).strip()


def source_state(root: Path) -> dict[str, str]:
    head = git(root, "rev-parse", "HEAD")
    status = git(root, "status", "--porcelain=v1", "--untracked-files=all")
    digest = hashlib.sha256(subprocess.check_output(("git", "diff", "HEAD", "--binary"), cwd=root))
    flags = subprocess.check_output(("git", "ls-files", "-v", "-z"), cwd=root)
    hidden = [os.fsdecode(entry) for entry in flags.split(b"\0") if entry and (entry[:1].islower() or entry[:1] == b"S")]
    if hidden:
        status += "\nIndex flags can hide edits: " + "; ".join(hidden)
    digest.update(flags)
    untracked = subprocess.check_output(("git", "ls-files", "--others", "--exclude-standard", "-z"), cwd=root)
    for name in untracked.split(b"\0"):
        if name:
            path = root / os.fsdecode(name)
            digest.update(name)
            digest.update(path.read_bytes() if not path.is_symlink() else os.readlink(path).encode())
    return {"head": head, "status": redact(status), "digest": digest.hexdigest()}


def workflow_coverage(root: Path) -> str:
    workflows = root / ".github/workflows"
    try:
        actual = {p.name: hashlib.sha256(p.read_bytes()).hexdigest() for p in workflows.glob("*") if p.suffix in {".yml", ".yaml"}}
    except OSError as error:
        return f"Cannot inspect required workflows: {redact(str(error))}"
    if actual != WORKFLOW_HASHES:
        return "Workflow inventory changed. Review every job/step, update CHECKS, exclusions, tests, and WORKFLOW_HASHES."
    return ""


def run_check(check: Check, root: Path, output: Path, timeout: float) -> Result:
    start = time.monotonic()
    command = shlex.join(check.command)
    log = f"{check.name}.log"
    status, detail, captured = "failed", "", ""
    try:
        process = subprocess.Popen(check.command, cwd=root / check.directory, stdout=subprocess.PIPE,
                                   stderr=subprocess.STDOUT, text=True, errors="replace", start_new_session=True)
        try:
            captured, _ = process.communicate(timeout=timeout)
            status = "passed" if process.returncode == 0 else "failed"
            detail = f"exit {process.returncode}"
        except subprocess.TimeoutExpired:
            status, detail = "timed-out", f"timeout after {timeout:g}s; process-group termination attempted"
            try:
                os.killpg(process.pid, signal.SIGKILL)
            except (ProcessLookupError, PermissionError):
                pass
            try:
                captured, _ = process.communicate(timeout=TERMINATION_TIMEOUT)
            except subprocess.TimeoutExpired as error:
                captured = error.output or b""
                if isinstance(captured, bytes):
                    captured = captured.decode(errors="replace")
                process.stdout.close()
                try:
                    process.wait(timeout=TERMINATION_TIMEOUT)
                except subprocess.TimeoutExpired:
                    detail += "; command could not be reaped within the termination deadline"
                detail += "; detached descendants may still be running"
    except OSError as error:
        status, detail = "unavailable", str(error)
    try:
        if status == "passed" and check.name.endswith("-version"):
            detail += "; " + redact(captured.splitlines()[0] if captured.splitlines() else "no version output")
            if check.name == "elide-version":
                expected = (root / ".elideversion").read_text().strip()
                if not re.search(rf"(?<![\d.]){re.escape(expected)}(?:\+[^\s]+)?(?=\s|$)", captured):
                    status, detail = "failed", f"Elide version does not match required {expected}"
            elif check.name == "bun-version":
                expected = json.loads((root / "frontend/package.json").read_text())["packageManager"].removeprefix("bun@")
                if captured.strip() != expected:
                    status, detail = "failed", f"bun version does not match required {expected}"
            elif check.name == "python-version":
                version = re.search(r"Python (\d+)\.(\d+)", captured)
                if not version or tuple(map(int, version.groups())) < MIN_PYTHON_VERSION:
                    status, detail = "failed", "Python 3.11+ is required"
    except (OSError, KeyError, ValueError) as error:
        status, detail = "unavailable", f"Cannot validate toolchain prerequisite: {redact(str(error))}"
    seconds = time.monotonic() - start
    (output / log).write_text(redact(f"cwd: {check.directory}\n$ {command}\n{detail}\n{captured}"))
    return Result(check.name, command, check.directory, check.ci, status, seconds, redact(detail), log)


def run_checks(checks: Sequence[Check], root: Path, output: Path, timeout: float, source: dict | None = None) -> list[Result]:
    results = []
    drift = []
    for check in checks:
        if source is not None and source_state(root) != source:
            drift.append(f"Source changed before {check.name}")
        result = run_check(check, root, output, timeout)
        if source is not None and source_state(root) != source:
            drift.append(f"Source changed after {check.name}")
        results.append(result)
        print(f"{'✅' if result.status == 'passed' else '❌'} {result.name}: {result.status} ({result.seconds:.2f}s)", flush=True)
        print(f"  [{result.directory}] {result.command}\n  {result.detail}", flush=True)
        if result.status != "passed":
            print("\n".join((output / result.log).read_text().splitlines()[-FAILURE_TAIL_LINES:]), flush=True)
    if drift:
        detail = "; ".join(drift) + "; rerun on stable source"
        (output / "source-stability.log").write_text(redact(detail) + "\n")
        results.append(Result("source-stability", "source snapshots before and after every command", ".", "evidence integrity", "failed", 0.0, detail, "source-stability.log"))
        print(f"❌ source-stability: {detail}", flush=True)
    return results


def render_summary(evidence: dict) -> str:
    results = evidence["results"]
    passed = sum(result["status"] == "passed" for result in results)
    source = evidence["source"]
    lines = ["## SporeSync local verification", "", f"Verification performed on behalf of @{escaped(evidence['username'])}.",
             "Supplied metadata is attribution, not a verified identity.",
             f"Harness {metadata(evidence['harness'])}. Model {metadata(evidence['model'])}. Effort {metadata(evidence['effort'])}.",
             f"Source SHA `{source['head']}`. Working tree {'dirty' if source['status'] else 'clean'}.",
             f"Started {metadata(evidence['started'])}. Duration {evidence['seconds']:.2f}s.",
             f"{'✅' if passed == len(results) else '❌'} **{passed}/{len(results)} required checks passed.**", "",
             "| Check | Result | Exact command | Directory | CI mapping |", "|---|---|---|---|---|"]
    for result in results:
        icon = "✅" if result["status"] == "passed" else "❌"
        lines.append(f"| {escaped(result['name'])} | {icon} {escaped(result['status'])}, {escaped(result['detail'])} | "
                     f"<code>{escaped(result['command'])}</code> | {escaped(result['directory'])} | {escaped(result['ci'])} |")
    failures = [f"{r['name']} ({r['status']}; {r['detail']})" for r in results if r["status"] != "passed"]
    lines += ["", "Failures: " + (escaped("; ".join(failures)) if failures else "none."), "", EXCLUSIONS,
              "", "Local results do not replace hosted CI or review. Logs remain local and are not included in published summaries."]
    return "\n".join(lines) + "\n"


def write_evidence(output: Path, evidence: dict) -> str:
    summary = render_summary(evidence)
    (output / "evidence.json").write_text(json.dumps(evidence, indent=2) + "\n")
    (output / "summary.md").write_text(summary)
    report = summary + "\n## Local evidence\n\n"
    report += "Working tree status at start\n\n<pre>" + escaped(evidence["source"]["status"] or "clean") + "</pre>\n\n"
    report += "| Check | Seconds | Log |\n|---|---|---|\n"
    for result in evidence["results"]:
        report += f"| {escaped(result['name'])} | {result['seconds']:.2f} | [{result['log']}]({result['log']}) |\n"
    for result in evidence["results"]:
        if result["status"] != "passed":
            tail = "\n".join((output / result["log"]).read_text().splitlines()[-FAILURE_TAIL_LINES:])
            report += f"\n<details><summary>{escaped(result['name'])} failure</summary><pre>{escaped(tail)}</pre></details>\n\n"
    (output / "report.md").write_text(report)
    files = ["evidence.json", "summary.md", *[result["log"] for result in evidence["results"]]]
    integrity = {name: hashlib.sha256((output / name).read_bytes()).hexdigest() for name in files}
    (output / "integrity.json").write_text(json.dumps(integrity, indent=2) + "\n")
    return summary


def validate_evidence(output: Path, evidence: dict) -> None:
    expected = ["ci-coverage", *[check.name for check in CHECKS]]
    names = [result["name"] for result in evidence["results"]]
    if names not in (expected, [*expected, "source-stability"]):
        raise ValueError("Saved evidence does not contain the complete required check inventory")
    if not re.fullmatch(USERNAME_PATTERN, evidence["username"]):
        raise ValueError("Saved attribution contains an invalid username")
    datetime.fromisoformat(evidence["started"])
    if not math.isfinite(evidence["seconds"]) or evidence["seconds"] < 0:
        raise ValueError("Saved duration must be finite and nonnegative")
    for result in evidence["results"]:
        if result["status"] not in {"passed", "failed", "unavailable", "timed-out"}:
            raise ValueError("Saved evidence contains an invalid status")
        if not math.isfinite(result["seconds"]) or result["seconds"] < 0:
            raise ValueError("Saved check duration must be finite and nonnegative")
        if result["log"] != result["name"] + ".log":
            raise ValueError("Saved evidence contains an unexpected log path")
        if result["name"] not in {"ci-coverage", "source-stability"}:
            header = (output / result["log"]).read_text().splitlines()
            if len(header) < 3 or header[2] != result["detail"]:
                raise ValueError("Saved status detail disagrees with its command log")
            if result["status"] == "passed" and not result["detail"].startswith("exit 0"):
                raise ValueError("Saved success disagrees with its command log")
    files = ["evidence.json", "summary.md", *[result["log"] for result in evidence["results"]]]
    actual = {name: hashlib.sha256((output / name).read_bytes()).hexdigest() for name in files}
    if json.loads((output / "integrity.json").read_text()) != actual:
        raise ValueError("Saved evidence or logs changed after verification; rerun")


def gh_api(endpoint: str, *, payload: dict | None = None, graphql: bool = False) -> dict | list:
    command = ["gh", "api", "--hostname", "github.com", endpoint]
    if payload is not None:
        command += ["--method", "POST", "--input", "-"]
    response = subprocess.run(command, input=json.dumps(payload) if payload is not None else None,
                              capture_output=True, text=True, timeout=GH_TIMEOUT)
    if response.returncode:
        raise ValueError(f"GitHub API failed for {endpoint} (exit {response.returncode}); no response body logged")
    data = json.loads(response.stdout)
    if graphql and data.get("errors"):
        raise ValueError("GitHub relationship query failed")
    return data


def resolve_destinations(pr: int, source: dict[str, str]) -> list[dict]:
    query = """query($number: Int!, $limit: Int!) {
      repository(owner: "IanWorley", name: "SporeSync") {
        nameWithOwner
        pullRequest(number: $number) {
          number url state headRefOid headRepository { nameWithOwner }
          closingIssuesReferences(first: $limit) {
            pageInfo { hasNextPage }
            nodes { number url repository { nameWithOwner } }
          }
        }
      }
    }"""
    data = gh_api("graphql", payload={"query": query, "variables": {"number": pr, "limit": GH_PAGE_SIZE}}, graphql=True)
    repo = data["data"]["repository"]
    pull = repo["pullRequest"]
    if (repo["nameWithOwner"] != REPOSITORY or pull is None or pull["number"] != pr
            or pull["url"] != f"https://github.com/{REPOSITORY}/pull/{pr}"
            or pull["state"] != "OPEN" or pull["headRefOid"] != source["head"]
            or pull["headRepository"]["nameWithOwner"] != REPOSITORY or source["status"]):
        raise ValueError("PR must be open in IanWorley/SporeSync with the same clean verified head and source repository")
    issues = pull["closingIssuesReferences"]
    if issues["pageInfo"]["hasNextPage"]:
        raise ValueError("Linked issue list exceeds safety bound; refusing partial destinations")
    destinations = [{"kind": "PR", "number": pr, "url": pull["url"]}]
    for issue in issues["nodes"]:
        number = issue["number"]
        if (type(number) is not int or number <= 0 or issue["repository"]["nameWithOwner"] != REPOSITORY
                or issue["url"] != f"https://github.com/{REPOSITORY}/issues/{number}"):
            raise ValueError("Linked issue is outside the allowed repository or has an invalid target")
        actual = gh_api(f"repos/{REPOSITORY}/issues/{number}")
        if "pull_request" in actual or actual["number"] != number or actual["html_url"] != issue["url"]:
            raise ValueError("Linked target is not the expected GitHub issue")
        destinations.append({"kind": "issue", "number": number, "url": issue["url"]})
    return destinations


def prepare_publication(root: Path, output: Path, evidence: dict, pr: int) -> tuple[str, list[dict]]:
    if source_state(root) != evidence["source"]:
        raise ValueError("Source changed since verification; rerun before publishing")
    destinations = resolve_destinations(pr, evidence["source"])
    targets = "\n".join(f"- {destination['kind']}: {destination['url']}" for destination in destinations)
    report_digest = hashlib.sha256((render_summary(evidence) + targets).encode()).hexdigest()
    marker = f"<!-- sporesync-local-ci pr={pr} sha={evidence['source']['head']} report={report_digest} -->"
    issue_note = "\nLinked issue comments: not applicable; GitHub reports no linked closing issues.\n" if len(destinations) == 1 else ""
    summary = render_summary(evidence) + "\nComment destinations approved together:\n\n" + targets + "\n" + issue_note + "\n" + marker + "\n"
    (output / "comment.md").write_text(summary)
    (output / "destinations.json").write_text(json.dumps(destinations, indent=2) + "\n")
    digest = hashlib.sha256(summary.encode()).hexdigest()
    print(f"Comment preview: {output / 'comment.md'}\nApproval SHA256: {digest}")
    for destination in destinations:
        print(f"Proposed {destination['kind']} comment: {destination['url']}")
    if len(destinations) == 1:
        print("Linked issue comments: not applicable; GitHub reports no linked closing issues.")
    return summary, destinations


def publish(root: Path, output: Path, evidence: dict, pr: int, approval: str) -> bool:
    approved_destinations = json.loads((output / "destinations.json").read_text())
    summary, destinations = prepare_publication(root, output, evidence, pr)
    if approved_destinations != destinations:
        raise ValueError("Destinations changed since preview; obtain approval for the new destinations")
    if hashlib.sha256(summary.encode()).hexdigest() != approval:
        raise ValueError("Approval digest does not match the exact comment preview")
    marker = summary.splitlines()[-1]
    login = gh_api("user")["login"]
    outcomes = []
    for destination in destinations:
        outcome = {**destination, "status": "failed"}
        try:
            if resolve_destinations(pr, evidence["source"]) != destinations or source_state(root) != evidence["source"]:
                raise ValueError("Source or linked destinations changed; rerun and preview again")
            endpoint = f"repos/{REPOSITORY}/issues/{destination['number']}/comments"
            matches = []
            page = 1
            while True:
                comments = gh_api(f"{endpoint}?per_page={GH_PAGE_SIZE}&page={page}")
                matches.extend(c for c in comments if c["user"]["login"] == login and marker in (c.get("body") or ""))
                if len(comments) < GH_PAGE_SIZE:
                    break
                page += 1
            if any(c["body"] == summary for c in matches):
                comment = next(c for c in matches if c["body"] == summary)
                outcome["status"] = "already-present"
            elif matches:
                raise ValueError("A different summary already uses this PR/head marker; refusing to edit it")
            else:
                if resolve_destinations(pr, evidence["source"]) != destinations or source_state(root) != evidence["source"]:
                    raise ValueError("Source or linked destinations changed immediately before posting")
                comment = gh_api(endpoint, payload={"body": summary})
                outcome["status"] = "posted"
            url = comment["html_url"]
            if not url.startswith(destination["url"] + "#issuecomment-") or not url.rsplit("-", 1)[-1].isdigit():
                raise ValueError("GitHub returned an unexpected comment URL")
            outcome["comment_url"] = url
            print(f"{outcome['status']}: {url}")
        except (ValueError, KeyError, TypeError, OSError, subprocess.SubprocessError) as error:
            outcome["status"] = "failed"
            outcome["error"] = redact(str(error))
            print(f"❌ {destination['url']}: {outcome['error']}")
        outcomes.append(outcome)
        (output / "publication.json").write_text(json.dumps(outcomes, indent=2) + "\n")
    return all(outcome["status"] != "failed" for outcome in outcomes)


def positive_timeout(value: str) -> float:
    number = float(value)
    if not math.isfinite(number) or number <= 0:
        raise argparse.ArgumentTypeError("timeout must be a positive finite number")
    return number


def parser() -> argparse.ArgumentParser:
    cli = argparse.ArgumentParser(description=__doc__)
    cli.add_argument("--username", help="GitHub username on whose behalf verification runs")
    cli.add_argument("--harness", help="caller-supplied harness attribution")
    cli.add_argument("--model", help="caller-supplied model attribution")
    cli.add_argument("--effort", help="caller-supplied effort attribution")
    cli.add_argument("--timeout", type=positive_timeout, default=DEFAULT_TIMEOUT, help="seconds per command (default 900)")
    cli.add_argument("--output", type=Path, help="new evidence directory (default .verification/<UTC timestamp>)")
    cli.add_argument("--pr", type=int, help="SporeSync PR number; resolve destinations and save comment preview, no posting")
    cli.add_argument("--publish", type=Path, help="reuse an existing run directory after preview approval; does not rerun checks")
    cli.add_argument("--post-comment", action="store_true", help="explicitly send the approved summary to the PR and its linked issues")
    cli.add_argument("--approve-summary", help="SHA256 of the exact comment.md preview approved by the user")
    return cli


def main(argv: Sequence[str] | None = None) -> int:
    cli = parser()
    args = cli.parse_args(argv)
    if args.pr is not None and args.pr <= 0:
        cli.error("--pr must be a positive number in IanWorley/SporeSync")
    if args.post_comment and not (args.publish and args.pr and args.approve_summary):
        cli.error("posting requires --publish RUN_DIR --pr NUMBER --post-comment --approve-summary SHA256 after user approval")
    if args.approve_summary and not args.post_comment:
        cli.error("--approve-summary requires --post-comment")
    try:
        remote = git(ROOT, "remote", "get-url", "origin")
        if remote not in {f"https://github.com/{REPOSITORY}", f"https://github.com/{REPOSITORY}.git", f"git@github.com:{REPOSITORY}.git"}:
            raise ValueError("This entrypoint is restricted to IanWorley/SporeSync")
        if args.publish:
            if not args.pr:
                cli.error("--publish requires --pr")
            output = args.publish.resolve()
            evidence = json.loads((output / "evidence.json").read_text())
            validate_evidence(output, evidence)
        else:
            if not all((args.username, args.harness, args.model, args.effort)):
                cli.error("verification requires --username, --harness, --model, and --effort")
            if not re.fullmatch(USERNAME_PATTERN, args.username):
                cli.error("--username must be a GitHub username without @")
            started = datetime.now(timezone.utc)
            output = (args.output or ROOT / ".verification" / started.strftime("%Y%m%dT%H%M%S.%fZ")).resolve()
            output.mkdir(parents=True, exist_ok=False)
            source = source_state(ROOT)
            start = time.monotonic()
            error = workflow_coverage(ROOT)
            (output / "ci-coverage.log").write_text(error or "Workflow inventory matches the reviewed command map.\n")
            coverage = Result("ci-coverage", "internal workflow fingerprint validation (hashlib.sha256)", ".", "all workflow files / fail closed on drift", "failed" if error else "passed", 0.0, error or "workflow map matches", "ci-coverage.log")
            print(f"{'❌' if error else '✅'} ci-coverage: {coverage.detail}", flush=True)
            results = [coverage, *run_checks(CHECKS, ROOT, output, args.timeout, source)]
            if source_state(ROOT) != source and not any(result.name == "source-stability" for result in results):
                detail = "HEAD or working tree changed during verification; rerun on stable source"
                (output / "source-stability.log").write_text(detail + "\n")
                results.append(Result("source-stability", "git status; git diff HEAD", ".", "evidence integrity", "failed", 0.0, detail, "source-stability.log"))
            evidence = {"username": args.username, "harness": redact(args.harness), "model": redact(args.model), "effort": redact(args.effort), "source": source, "started": started.isoformat(), "seconds": time.monotonic() - start, "results": [asdict(result) for result in results]}
            write_evidence(output, evidence)
        passed = sum(result["status"] == "passed" for result in evidence["results"])
        print(f"{passed}/{len(evidence['results'])} required checks passed. Report: {output / 'report.md'}")
        publication_ok = True
        if args.post_comment:
            publication_ok = publish(ROOT, output, evidence, args.pr, args.approve_summary)
        elif args.pr:
            prepare_publication(ROOT, output, evidence, args.pr)
        return 0 if passed == len(evidence["results"]) and publication_ok else 1
    except (ValueError, KeyError, TypeError, OSError, subprocess.SubprocessError) as error:
        print(f"❌ {redact(str(error))}", file=sys.stderr)
        return 1


if __name__ == "__main__":
    sys.exit(main())
