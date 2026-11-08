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
WORKFLOW_HASHES = {"ci.yml": "df0d35855dbfaea293d3190cb5e90730091bb18be49400faeb640c0689f86b8a"}
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
    Check("runner-tests", (sys.executable, "-m", "unittest", "discover", "-s", "scripts", "-p", "test_*.py", "-v"), ".", "ci.yml / verifier"),
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
    for key, secret in os.environ.items():
        if re.search(r"TOKEN|SECRET|PASSWORD|CREDENTIAL|API_KEY", key, re.I) and len(secret) >= MIN_SECRET_LENGTH:
            value = value.replace(secret, "[REDACTED]")
    value = re.sub(r"\x1b\[[0-?]*[ -/]*[@-~]", "", value)
    value = re.sub(r"(?:gh[pousr]_[A-Za-z0-9_]+|github_pat_[A-Za-z0-9_]+)", "[REDACTED]", value)
    value = re.sub(r"(?i)(authorization\s*[:=]\s*)([^\r\n]+)", r"\1[REDACTED]", value)
    value = re.sub(r"(https?://)[^\s/@]+:[^\s/@]+@", r"\1[REDACTED]@", value)
    value = re.sub(r"(?i)((?:token|password|secret|api[_-]?key)\s*[:=]\s*)[^\s,;]+", r"\1[REDACTED]", value)
    return value


def escaped(value: str) -> str:
    return html.escape(redact(value), quote=True).replace("|", "&#124;").replace("`", "&#96;").replace("\n", "<br>")


def git(root: Path, *args: str) -> str:
    return subprocess.check_output(("git", *args), cwd=root, text=True, stderr=subprocess.PIPE).strip()


def source_state(root: Path) -> dict[str, str]:
    head = git(root, "rev-parse", "HEAD")
    status = git(root, "status", "--porcelain=v1", "--untracked-files=all")
    digest = hashlib.sha256(subprocess.check_output(("git", "diff", "HEAD", "--binary"), cwd=root))
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
            os.killpg(process.pid, signal.SIGKILL)
            captured, _ = process.communicate()
            status, detail = "timed-out", f"timeout after {timeout:g}s; process group killed"
    except OSError as error:
        status, detail = "unavailable", str(error)
    seconds = time.monotonic() - start
    (output / log).write_text(redact(f"cwd: {check.directory}\n$ {command}\n{detail}\n{captured}"))
    return Result(check.name, command, check.directory, check.ci, status, seconds, redact(detail), log)


def run_checks(checks: Sequence[Check], root: Path, output: Path, timeout: float) -> list[Result]:
    results = []
    for check in checks:
        result = run_check(check, root, output, timeout)
        results.append(result)
        print(f"{'✅' if result.status == 'passed' else '❌'} {result.name}: {result.status} ({result.seconds:.2f}s)", flush=True)
        print(f"  [{result.directory}] {result.command}\n  {result.detail}", flush=True)
        if result.status != "passed":
            print("\n".join((output / result.log).read_text().splitlines()[-FAILURE_TAIL_LINES:]), flush=True)
    return results


def render_summary(evidence: dict) -> str:
    results = evidence["results"]
    passed = sum(result["status"] == "passed" for result in results)
    source = evidence["source"]
    lines = ["## SporeSync local verification", "", f"Verification performed on behalf of @{escaped(evidence['username'])}.",
             "Supplied metadata is attribution, not a verified identity.",
             f"Harness {escaped(evidence['harness'])}. Model {escaped(evidence['model'])}. Effort {escaped(evidence['effort'])}.",
             f"Source SHA `{source['head']}`. Working tree {'dirty' if source['status'] else 'clean'}.",
             f"Started {evidence['started']}. Duration {evidence['seconds']:.2f}s.",
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
    return summary


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
    marker = f"<!-- sporesync-local-ci pr={pr} sha={evidence['source']['head']} -->"
    targets = "\n".join(f"- {destination['kind']}: {destination['url']}" for destination in destinations)
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
            if source_state(root) != evidence["source"] or resolve_destinations(pr, evidence["source"]) != destinations:
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
        else:
            if not all((args.username, args.harness, args.model, args.effort)):
                cli.error("verification requires --username, --harness, --model, and --effort")
            if not re.fullmatch(r"[A-Za-z0-9](?:[A-Za-z0-9-]{0,37}[A-Za-z0-9])?", args.username):
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
            results = [coverage, *run_checks(CHECKS, ROOT, output, args.timeout)]
            if source_state(ROOT) != source:
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
