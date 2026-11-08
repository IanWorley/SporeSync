from __future__ import annotations

import contextlib
import copy
import hashlib
import io
import json
import os
from pathlib import Path
import subprocess
import sys
import tempfile
import unittest
from unittest.mock import patch

import verify_ci as runner


class FakeGitHub:
    def __init__(self, head: str, issues: tuple[int, ...] = (7,)) -> None:
        self.pr = 80
        self.pull = {
            "number": self.pr, "url": f"https://github.com/{runner.REPOSITORY}/pull/{self.pr}",
            "state": "OPEN", "headRefOid": head,
            "headRepository": {"nameWithOwner": runner.REPOSITORY},
            "closingIssuesReferences": {"pageInfo": {"hasNextPage": False}, "nodes": [
                {"number": number, "url": f"https://github.com/{runner.REPOSITORY}/issues/{number}",
                 "repository": {"nameWithOwner": runner.REPOSITORY}} for number in issues]},
        }
        self.comments: dict[int, list[dict]] = {}
        self.fail: set[int] = set()
        self.writes: list[tuple[int, str]] = []

    def __call__(self, endpoint: str, *, payload: dict | None = None, graphql: bool = False) -> dict | list:
        if endpoint == "graphql":
            return {"data": {"repository": {"nameWithOwner": runner.REPOSITORY, "pullRequest": self.pull}}}
        if endpoint == "user":
            return {"login": "authenticated-user"}
        number = int(endpoint.split("/")[4])
        if "/comments" not in endpoint:
            return {"number": number, "html_url": f"https://github.com/{runner.REPOSITORY}/issues/{number}"}
        if payload is None:
            return self.comments.get(number, [])
        if number in self.fail:
            raise ValueError("simulated posting failure")
        self.writes.append((number, payload["body"]))
        target = "pull" if number == self.pr else "issues"
        comment = {"body": payload["body"], "user": {"login": "authenticated-user"},
                   "html_url": f"https://github.com/{runner.REPOSITORY}/{target}/{number}#issuecomment-{len(self.writes)}"}
        self.comments.setdefault(number, []).append(comment)
        return comment


class RunnerTests(unittest.TestCase):
    def setUp(self) -> None:
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        self.root = Path(self.temp.name)
        self.output = self.root / "logs"
        self.output.mkdir()

    def check(self, name: str, code: str) -> runner.Check:
        return runner.Check(name, (sys.executable, "-c", code), ".", "test fixture")

    def test_success_counts_and_failure_continues_with_named_nonzero(self) -> None:
        checks = (self.check("broken", "print('specific failure'); raise SystemExit(9)"),
                  self.check("later", "print('ran after failure')"))
        stream = io.StringIO()
        with contextlib.redirect_stdout(stream):
            results = runner.run_checks(checks, self.root, self.output, runner.GH_TIMEOUT)
        self.assertEqual([r.status for r in results], ["failed", "passed"])
        self.assertEqual(sum(r.status == "passed" for r in results), 1)
        self.assertIn("❌ broken", stream.getvalue())
        self.assertIn("✅ later", stream.getvalue())
        self.assertIn("exit 9", stream.getvalue())
        self.assertIn("specific failure", (self.output / "broken.log").read_text())
        self.assertIn("ran after failure", (self.output / "later.log").read_text())

    def test_timeout_and_unavailable_are_failures_and_execution_continues(self) -> None:
        checks = (self.check("slow", "import time; time.sleep(30)"),
                  runner.Check("missing", (str(self.root / "absent"),), ".", "fixture"),
                  self.check("later", "print('still ran')"))
        with contextlib.redirect_stdout(io.StringIO()):
            results = runner.run_checks(checks, self.root, self.output, 0.1)
        self.assertEqual([r.status for r in results], ["timed-out", "unavailable", "passed"])

    def test_cli_timeout_and_unavailable_produce_nonzero_and_saved_failures(self) -> None:
        checks = (self.check("slow", "import time; time.sleep(30)"),
                  runner.Check("missing", (str(self.root / "absent"),), ".", "fixture"),
                  self.check("later", "print('continued')"))
        output = self.root / "cli-failures"
        stream = io.StringIO()
        with patch.object(runner, "ROOT", self.root), patch.object(runner, "CHECKS", checks), patch.object(runner, "git", return_value=f"https://github.com/{runner.REPOSITORY}"), patch.object(runner, "source_state", return_value=self.evidence()["source"]), patch.object(runner, "workflow_coverage", return_value=""), contextlib.redirect_stdout(stream):
            result = runner.main(["--username", "IanWorley", "--harness", "test", "--model", "test", "--effort", "test", "--timeout", "0.1", "--output", str(output)])
        self.assertEqual(result, 1)
        self.assertIn("2/4 required checks passed", stream.getvalue())
        report = (output / "report.md").read_text()
        self.assertIn("slow (timed-out;", report)
        self.assertIn("missing (unavailable;", report)
        self.assertIn("✅ passed", report)

    def test_logs_and_metadata_escape_html_markdown_and_redact_secrets(self) -> None:
        secret = "sensitive-value-for-test"
        with patch.dict(os.environ, {"TEST_API_KEY": secret}):
            result = runner.run_check(self.check("output", f"print({secret!r}); print('Authorization: Bearer confidential')"), self.root, self.output, runner.GH_TIMEOUT)
            evidence = self.evidence()
            evidence["harness"] = f"<script> | ` $(touch sentinel) {secret}"
            summary = runner.write_evidence(self.output, evidence)
        log = (self.output / result.log).read_text()
        self.assertNotIn(secret, log + summary)
        self.assertNotIn("confidential", log)
        self.assertNotIn("<script>", summary)
        self.assertIn("&lt;script&gt; &#124; &#96;", summary)
        self.assertIn("not a verified identity", summary)
        self.assertFalse((self.root / "sentinel").exists())

    def test_workflow_inventory_fails_closed_for_changed_and_new_files(self) -> None:
        workflows = self.root / ".github/workflows"
        workflows.mkdir(parents=True)
        original = runner.ROOT / ".github/workflows/ci.yml"
        (workflows / "ci.yml").write_bytes(original.read_bytes())
        self.assertEqual(runner.workflow_coverage(self.root), "")
        (workflows / "ci.yml").write_text("jobs: {}")
        self.assertIn("changed", runner.workflow_coverage(self.root))
        (workflows / "ci.yml").write_bytes(original.read_bytes())
        (workflows / "new.yaml").write_text("jobs: {}")
        self.assertIn("changed", runner.workflow_coverage(self.root))

    def evidence(self) -> dict:
        return {"username": "IanWorley", "harness": "T3 Code / Codex", "model": "gpt-6.1-sol", "effort": "high",
                "source": {"head": "a" * 40, "status": "", "digest": "fixture"},
                "started": "2026-11-08T00:00:00+00:00", "seconds": 1.0,
                "results": [runner.asdict(runner.Result("test", "test --check", ".", "fixture", "passed", 1.0, "exit 0", "test.log"))]}

    def test_opt_in_preview_no_writes_and_no_issues_not_applicable(self) -> None:
        evidence = self.evidence()
        fake = FakeGitHub(evidence["source"]["head"], ())
        stream = io.StringIO()
        with patch.object(runner, "source_state", return_value=evidence["source"]), patch.object(runner, "gh_api", fake), contextlib.redirect_stdout(stream):
            summary, destinations = runner.prepare_publication(self.root, self.output, evidence, fake.pr)
        self.assertEqual(len(destinations), 1)
        self.assertEqual(fake.writes, [])
        self.assertIn("not applicable", stream.getvalue())
        self.assertEqual(summary, (self.output / "comment.md").read_text())

    def test_partial_posting_failure_retries_idempotently_with_matching_summary(self) -> None:
        evidence = self.evidence()
        evidence["results"][0].update(status="failed", detail="exit 9")
        fake = FakeGitHub(evidence["source"]["head"])
        fake.fail = {7}
        with patch.object(runner, "source_state", return_value=evidence["source"]), patch.object(runner, "gh_api", fake), contextlib.redirect_stdout(io.StringIO()):
            summary, _ = runner.prepare_publication(self.root, self.output, evidence, fake.pr)
            approval = hashlib.sha256(summary.encode()).hexdigest()
            self.assertFalse(runner.publish(self.root, self.output, evidence, fake.pr, approval))
            first = json.loads((self.output / "publication.json").read_text())
            self.assertEqual([r["status"] for r in first], ["posted", "failed"])
            fake.fail.clear()
            self.assertTrue(runner.publish(self.root, self.output, evidence, fake.pr, approval))
        self.assertEqual([number for number, _ in fake.writes], [fake.pr, 7])
        self.assertEqual(fake.writes[0][1], fake.writes[1][1])
        self.assertIn("❌ **0/1", summary)
        self.assertIn("test (failed; exit 9)", summary)
        final = json.loads((self.output / "publication.json").read_text())
        self.assertEqual([r["status"] for r in final], ["already-present", "posted"])
        self.assertTrue(all("#issuecomment-" in r["comment_url"] for r in final))

    def test_rejects_unapproved_summary_stale_dirty_foreign_or_untrusted_targets(self) -> None:
        evidence = self.evidence()
        for mutation in ("head", "dirty", "foreign", "url", "pagination", "issue", "closed", "fork", "missing"):
            with self.subTest(mutation=mutation):
                source = copy.deepcopy(evidence["source"])
                fake = FakeGitHub(source["head"])
                if mutation == "head":
                    fake.pull["headRefOid"] = "b" * 40
                elif mutation == "dirty":
                    source["status"] = " M file"
                elif mutation == "foreign":
                    fake.pull["closingIssuesReferences"]["nodes"][0]["repository"]["nameWithOwner"] = "other/repo"
                elif mutation == "url":
                    fake.pull["url"] = "https://evil.test/80"
                elif mutation == "pagination":
                    fake.pull["closingIssuesReferences"]["pageInfo"]["hasNextPage"] = True
                elif mutation == "issue":
                    fake.pull["closingIssuesReferences"]["nodes"][0]["url"] = "https://github.com/IanWorley/SporeSync/issues/999"
                elif mutation == "closed":
                    fake.pull["state"] = "CLOSED"
                elif mutation == "fork":
                    fake.pull["headRepository"]["nameWithOwner"] = "attacker/SporeSync"
                elif mutation == "missing":
                    fake.pull = None
                with patch.object(runner, "gh_api", fake), self.assertRaises(ValueError):
                    runner.resolve_destinations(fake.pr, source)
                self.assertEqual(fake.writes, [])
        fake = FakeGitHub(evidence["source"]["head"])
        (self.output / "destinations.json").write_text(json.dumps([{"kind": "PR", "number": fake.pr, "url": fake.pull["url"]}, {"kind": "issue", "number": 7, "url": fake.pull["closingIssuesReferences"]["nodes"][0]["url"]}]))
        with patch.object(runner, "source_state", return_value=evidence["source"]), patch.object(runner, "gh_api", fake), contextlib.redirect_stdout(io.StringIO()), self.assertRaisesRegex(ValueError, "Approval"):
            runner.publish(self.root, self.output, evidence, fake.pr, "not-approved")
        self.assertEqual(fake.writes, [])
        with patch.object(runner, "source_state", return_value={}), self.assertRaisesRegex(ValueError, "Source changed"):
            runner.prepare_publication(self.root, self.output, evidence, fake.pr)

    def test_changed_destinations_reject_old_approval_on_every_retry(self) -> None:
        evidence = self.evidence()
        fake = FakeGitHub(evidence["source"]["head"])
        with patch.object(runner, "source_state", return_value=evidence["source"]), patch.object(runner, "gh_api", fake), contextlib.redirect_stdout(io.StringIO()):
            summary, _ = runner.prepare_publication(self.root, self.output, evidence, fake.pr)
            approval = hashlib.sha256(summary.encode()).hexdigest()
            fake.pull["closingIssuesReferences"]["nodes"] = [{"number": 8,
                "url": f"https://github.com/{runner.REPOSITORY}/issues/8",
                "repository": {"nameWithOwner": runner.REPOSITORY}}]
            for _ in range(2):
                with self.assertRaises(ValueError):
                    runner.publish(self.root, self.output, evidence, fake.pr, approval)
            self.assertEqual(fake.writes, [])

    def test_forged_existing_comments_are_not_overwritten_or_used_for_deduplication(self) -> None:
        evidence = self.evidence()
        fake = FakeGitHub(evidence["source"]["head"], ())
        with patch.object(runner, "source_state", return_value=evidence["source"]), patch.object(runner, "gh_api", fake), contextlib.redirect_stdout(io.StringIO()):
            summary, _ = runner.prepare_publication(self.root, self.output, evidence, fake.pr)
            fake.comments[fake.pr] = [{"body": summary, "user": {"login": "someone-else"}, "html_url": "untrusted"}]
            self.assertTrue(runner.publish(self.root, self.output, evidence, fake.pr, hashlib.sha256(summary.encode()).hexdigest()))
        self.assertEqual(len(fake.writes), 1)

    def test_gh_transport_uses_stdin_and_never_logs_api_error_secrets(self) -> None:
        binary = self.root / "gh"
        binary.write_text(f"#!{sys.executable}\nimport sys\nassert '--input' in sys.argv\nprint('secret-response', file=sys.stderr)\nraise SystemExit(1)\n")
        binary.chmod(0o755)
        with patch.dict(os.environ, {"PATH": str(self.root)}), self.assertRaises(ValueError) as caught:
            runner.gh_api("graphql", payload={"query": "test"})
        self.assertNotIn("secret-response", str(caught.exception))

    def test_cli_help_opt_in_and_timeout_validation(self) -> None:
        cli = runner.ROOT / "scripts/verify_ci.py"
        help_result = subprocess.run((str(cli), "--help"), capture_output=True, text=True)
        self.assertEqual(help_result.returncode, 0)
        self.assertIn("--post-comment", help_result.stdout)
        for args in (("--post-comment",), ("--timeout", "nan"), ("--timeout", "0"), ("--timeout", "inf")):
            result = subprocess.run((sys.executable, str(cli), *args), capture_output=True, text=True)
            self.assertNotEqual(result.returncode, 0)

    def test_real_cli_success_failure_counts_and_markdown_using_fake_toolchains(self) -> None:
        repo = self.root / "repo"
        (repo / "scripts").mkdir(parents=True)
        (repo / "frontend").mkdir()
        (repo / "backend").mkdir()
        (repo / ".github/workflows").mkdir(parents=True)
        (repo / "scripts/verify_ci.py").write_bytes((runner.ROOT / "scripts/verify_ci.py").read_bytes())
        (repo / "scripts/test_fixture.py").write_text("import unittest\nclass Fixture(unittest.TestCase):\n def test_behavior(self): self.assertEqual(1 + 1, 2)\n")
        (repo / ".github/workflows/ci.yml").write_bytes((runner.ROOT / ".github/workflows/ci.yml").read_bytes())
        for args in (("init", "-q"), ("config", "user.name", "Fixture"), ("config", "user.email", "fixture@example.test"),
                     ("remote", "add", "origin", f"https://github.com/{runner.REPOSITORY}"), ("add", "."), ("commit", "-qm", "fixture")):
            subprocess.run(("git", *args), cwd=repo, check=True, capture_output=True)
        for name in ("vp", "elide"):
            binary = self.root / name
            binary.write_text(f"#!{sys.executable}\nimport os,sys\nprint('fake toolchain', sys.argv[1:])\nraise SystemExit(9 if os.environ.get('FAIL_FIXTURE') and sys.argv[1:] in [['build'], ['lint']] else 0)\n")
            binary.chmod(0o755)
        args = (sys.executable, str(repo / "scripts/verify_ci.py"), "--username", "IanWorley", "--harness", "T3 Code / Codex", "--model", "gpt-6.1-sol", "--effort", "high")
        environment = {**os.environ, "PATH": str(self.root) + os.pathsep + os.environ["PATH"], "PYTHONDONTWRITEBYTECODE": "1"}
        success = self.root / "success"
        result = subprocess.run((*args, "--output", str(success)), env=environment, capture_output=True, text=True)
        self.assertEqual(result.returncode, 0, result.stdout + result.stderr)
        self.assertIn("12/12 required checks passed", result.stdout)
        self.assertIn("✅ **12/12", (success / "summary.md").read_text())
        environment["FAIL_FIXTURE"] = "yes"
        failure = self.root / "failure"
        result = subprocess.run((*args, "--output", str(failure)), env=environment, capture_output=True, text=True)
        self.assertEqual(result.returncode, 1, result.stdout + result.stderr)
        self.assertIn("9/12 required checks passed", result.stdout)
        for name in ("frontend-build", "backend-build", "frontend-lint"):
            self.assertIn(f"❌ {name}", result.stdout)
        self.assertIn("✅ frontend-format", result.stdout)
        self.assertIn("backend-build (failed; exit 9)", (failure / "report.md").read_text())
        self.assertFalse((failure / "publication.json").exists())


if __name__ == "__main__":
    unittest.main()
