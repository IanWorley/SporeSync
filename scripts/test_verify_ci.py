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


FAKE_PR_NUMBER = 80
LINKED_ISSUE_NUMBER = 7
CHANGED_ISSUE_NUMBER = 8
FAILURE_EXIT = 9
SHA_LENGTH = 40
TEST_TIMEOUT = 0.1


class FakeGitHub:
    def __init__(self, head: str, issues: tuple[int, ...] = (LINKED_ISSUE_NUMBER,)) -> None:
        self.pr = FAKE_PR_NUMBER
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
            results = runner.run_checks(checks, self.root, self.output, TEST_TIMEOUT)
        self.assertEqual([r.status for r in results], ["timed-out", "unavailable", "passed"])

    def test_cli_timeout_and_unavailable_produce_nonzero_and_saved_failures(self) -> None:
        checks = (self.check("slow", "import time; time.sleep(30)"),
                  runner.Check("missing", (str(self.root / "absent"),), ".", "fixture"),
                  self.check("later", "print('continued')"))
        output = self.root / "cli-failures"
        stream = io.StringIO()
        with patch.object(runner, "ROOT", self.root), patch.object(runner, "CHECKS", checks), patch.object(runner, "git", return_value=f"https://github.com/{runner.REPOSITORY}"), patch.object(runner, "source_state", return_value=self.evidence()["source"]), patch.object(runner, "workflow_coverage", return_value=""), contextlib.redirect_stdout(stream):
            result = runner.main(["--username", "IanWorley", "--harness", "test", "--model", "test", "--effort", "high", "--timeout", "0.1", "--output", str(output)])
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
        for name in runner.WORKFLOW_HASHES:
            (workflows / name).write_bytes((runner.ROOT / ".github/workflows" / name).read_bytes())
        self.assertEqual(runner.workflow_coverage(self.root), "")
        (workflows / "ci.yml").write_text("jobs: {}")
        self.assertIn("changed", runner.workflow_coverage(self.root))
        (workflows / "ci.yml").write_bytes(original.read_bytes())
        (workflows / "new.yaml").write_text("jobs: {}")
        self.assertIn("changed", runner.workflow_coverage(self.root))

    def evidence(self) -> dict:
        (self.output / "test.log").write_text("cwd: .\n$ test --check\nexit 0\n")
        return {"username": "IanWorley", "harness": "T3 Code / Codex", "model": "gpt-6.1-sol", "effort": "high",
                "source": {"head": "a" * SHA_LENGTH, "status": "", "digest": "fixture"},
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
        fake.fail = {LINKED_ISSUE_NUMBER}
        with patch.object(runner, "source_state", return_value=evidence["source"]), patch.object(runner, "gh_api", fake), contextlib.redirect_stdout(io.StringIO()):
            summary, _ = runner.prepare_publication(self.root, self.output, evidence, fake.pr)
            approval = hashlib.sha256(summary.encode()).hexdigest()
            self.assertFalse(runner.publish(self.root, self.output, evidence, fake.pr, approval))
            first = json.loads((self.output / "publication.json").read_text())
            self.assertEqual([r["status"] for r in first], ["posted", "failed"])
            fake.fail.clear()
            self.assertTrue(runner.publish(self.root, self.output, evidence, fake.pr, approval))
        self.assertEqual([number for number, _ in fake.writes], [fake.pr, LINKED_ISSUE_NUMBER])
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
                    fake.pull["headRefOid"] = "b" * SHA_LENGTH
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
        (self.output / "destinations.json").write_text(json.dumps([{"kind": "PR", "number": fake.pr, "url": fake.pull["url"]}, {"kind": "issue", "number": LINKED_ISSUE_NUMBER, "url": fake.pull["closingIssuesReferences"]["nodes"][0]["url"]}]))
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
            fake.pull["closingIssuesReferences"]["nodes"] = [{"number": CHANGED_ISSUE_NUMBER,
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

    def test_ansi_stripping_happens_before_secret_redaction(self) -> None:
        with patch.dict(os.environ, {"PROBE_SECRET": "ansi-sensitive-value"}):
            self.assertEqual(runner.redact("ansi-\x1b[1msensitive-value"), "[REDACTED]")

    def test_metadata_is_inert_single_line_text_without_mentions(self) -> None:
        evidence = self.evidence()
        evidence["harness"] = "T3\r\r**false pass** [approved](https://evil.test)"
        evidence["model"] = "cc @octocat and torvalds/linux#1"
        summary = runner.render_summary(evidence)
        self.assertNotIn("\r", summary)
        self.assertNotIn("**false pass**", summary)
        self.assertNotIn("[approved]", summary)
        self.assertNotIn("@octocat", summary)
        self.assertNotIn("torvalds/linux#1", summary)

    def test_hidden_index_flags_cannot_appear_clean(self) -> None:
        for flag in ("--assume-unchanged", "--skip-worktree"):
            with self.subTest(flag=flag), tempfile.TemporaryDirectory() as temporary:
                root = Path(temporary)
                (root / "file").write_text("original")
                for args in (("init", "-q"), ("config", "user.email", "fixture@example.test"), ("config", "user.name", "Fixture"), ("add", "."), ("commit", "-qm", "fixture"), ("update-index", flag, "file")):
                    subprocess.run(("git", *args), cwd=root, check=True, capture_output=True)
                (root / "file").write_text("hidden edit")
                self.assertIn("Index flags can hide edits", runner.source_state(root)["status"])

    def test_non_utf8_diff_is_hashed_without_aborting(self) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            (root / "file").write_text("original")
            for args in (("init", "-q"), ("config", "user.email", "fixture@example.test"), ("config", "user.name", "Fixture"), ("add", "."), ("commit", "-qm", "fixture")):
                subprocess.run(("git", *args), cwd=root, check=True, capture_output=True)
            before = runner.source_state(root)
            (root / "file").write_bytes(b"caf\xe9")
            after = runner.source_state(root)
            self.assertNotEqual(before["digest"], after["digest"])
            self.assertTrue(after["status"])

    def test_incomplete_or_modified_saved_evidence_is_rejected(self) -> None:
        evidence = self.evidence()
        names = ["ci-coverage", *[check.name for check in runner.CHECKS]]
        evidence["results"] = [runner.asdict(runner.Result(name, "fixture", ".", "fixture", "passed", 0.0, "exit 0", name + ".log")) for name in names]
        for result in evidence["results"]:
            (self.output / result["log"]).write_text("cwd: .\n$ fixture\nexit 0\n")
        runner.write_evidence(self.output, evidence)
        runner.validate_evidence(self.output, evidence)
        incomplete = copy.deepcopy(evidence)
        incomplete["results"].pop()
        with self.assertRaisesRegex(ValueError, "inventory"):
            runner.validate_evidence(self.output, incomplete)
        (self.output / "evidence.json").write_text("modified")
        with self.assertRaisesRegex(ValueError, "changed"):
            runner.validate_evidence(self.output, evidence)
        evidence["results"][-1]["detail"] = "exit 9"
        with self.assertRaisesRegex(ValueError, "log"):
            runner.validate_evidence(self.output, evidence)
        evidence["started"] = "<!-- fake -->"
        with self.assertRaises(ValueError):
            runner.validate_evidence(self.output, evidence)

    def test_second_approved_run_on_same_head_can_publish(self) -> None:
        evidence = self.evidence()
        fake = FakeGitHub(evidence["source"]["head"])
        with patch.object(runner, "source_state", return_value=evidence["source"]), patch.object(runner, "gh_api", fake), contextlib.redirect_stdout(io.StringIO()):
            for started in ("2026-11-08T00:00:00+00:00", "2026-11-08T01:00:00+00:00"):
                evidence["started"] = started
                summary, _ = runner.prepare_publication(self.root, self.output, evidence, fake.pr)
                self.assertTrue(runner.publish(self.root, self.output, evidence, fake.pr, hashlib.sha256(summary.encode()).hexdigest()))
        self.assertEqual(len(fake.writes), 4)

    def test_detached_pipe_timeout_keeps_output_and_finishes(self) -> None:
        pidfile = self.root / "detached.pid"
        code = f"import subprocess,sys; from pathlib import Path; p=subprocess.Popen([sys.executable,'-c','import time; time.sleep(30)'],start_new_session=True); Path({str(pidfile)!r}).write_text(str(p.pid)); print('leader done',flush=True)"
        try:
            with patch.object(runner.os, "killpg", side_effect=PermissionError("fixture")):
                result = runner.run_check(self.check("detached", code), self.root, self.output, TEST_TIMEOUT)
            self.assertEqual(result.status, "timed-out")
            self.assertIn("leader done", (self.output / result.log).read_text())
            self.assertIn("detached descendants", result.detail)
        finally:
            if pidfile.exists():
                try:
                    os.kill(int(pidfile.read_text()), runner.signal.SIGKILL)
                except ProcessLookupError:
                    pass

    def test_source_change_during_comment_lookup_prevents_posting(self) -> None:
        evidence = self.evidence()
        state = [evidence["source"]]
        fake = FakeGitHub(evidence["source"]["head"], ())
        def lookup(endpoint: str, **kwargs):
            result = fake(endpoint, **kwargs)
            if "/comments?" in endpoint:
                state[0] = {**evidence["source"], "digest": "changed"}
            return result
        with patch.object(runner, "source_state", side_effect=lambda root: state[0]), patch.object(runner, "gh_api", lookup), contextlib.redirect_stdout(io.StringIO()):
            summary, _ = runner.prepare_publication(self.root, self.output, evidence, fake.pr)
            self.assertFalse(runner.publish(self.root, self.output, evidence, fake.pr, hashlib.sha256(summary.encode()).hexdigest()))
        self.assertEqual(fake.writes, [])

    def test_source_drift_is_retained_even_if_restored_between_checks(self) -> None:
        source = {"head": "original"}
        checks = (self.check("first", "print('first')"), self.check("later", "print('later')"))
        snapshots = [source, {"head": "changed"}, source, source]
        with patch.object(runner, "source_state", side_effect=snapshots), contextlib.redirect_stdout(io.StringIO()):
            results = runner.run_checks(checks, self.root, self.output, runner.GH_TIMEOUT, source)
        self.assertEqual([result.status for result in results], ["passed", "passed", "failed"])
        self.assertEqual(results[-1].name, "source-stability")
        self.assertIn("after first", (self.output / "source-stability.log").read_text())

    def test_missing_pin_is_unavailable_and_later_checks_still_run(self) -> None:
        checks = (runner.Check("elide-version", (sys.executable, "-c", "print('1.6.0')"), ".", "fixture"), self.check("later", "print('continued')"))
        with contextlib.redirect_stdout(io.StringIO()):
            results = runner.run_checks(checks, self.root, self.output, runner.GH_TIMEOUT)
        self.assertEqual([result.status for result in results], ["unavailable", "passed"])

    def test_elide_version_must_match_the_ci_pin(self) -> None:
        (self.root / ".elideversion").write_text("1.6.0")
        for version, expected in (("1.6.0+fixture", "passed"), ("1.6.0-rc1", "failed"), ("2.0.0", "failed")):
            with self.subTest(version=version):
                check = runner.Check("elide-version", (sys.executable, "-c", f"print({version!r})"), ".", "fixture")
                self.assertEqual(runner.run_check(check, self.root, self.output, runner.GH_TIMEOUT).status, expected)

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
        for name in runner.WORKFLOW_HASHES:
            (repo / ".github/workflows" / name).write_bytes((runner.ROOT / ".github/workflows" / name).read_bytes())
        (repo / ".elideversion").write_text((runner.ROOT / ".elideversion").read_text())
        (repo / "frontend/package.json").write_text((runner.ROOT / "frontend/package.json").read_text())
        for args in (("init", "-q"), ("config", "user.name", "Fixture"), ("config", "user.email", "fixture@example.test"),
                     ("remote", "add", "origin", f"https://github.com/{runner.REPOSITORY}"), ("add", "."), ("commit", "-qm", "fixture")):
            subprocess.run(("git", *args), cwd=repo, check=True, capture_output=True)
        for name in ("vp", "elide", "bun"):
            binary = self.root / name
            binary.write_text(f"#!{sys.executable}\nimport os,sys\nfrom pathlib import Path\nversions = {{'elide': '1.6.0', 'bun': '1.3.14', 'vp': 'vp v0.3.0'}}\nprint(versions[Path(sys.argv[0]).name] if sys.argv[1:] == ['--version'] else 'fake toolchain')\nraise SystemExit(9 if os.environ.get('FAIL_FIXTURE') and sys.argv[1:] in [['build'], ['lint']] else 0)\n")
            binary.chmod(0o755)
        args = (sys.executable, str(repo / "scripts/verify_ci.py"), "--username", "IanWorley", "--harness", "T3 Code / Codex", "--model", "gpt-6.1-sol", "--effort", "high")
        required_count = 1 + len(runner.CHECKS)
        environment = {**os.environ, "PATH": str(self.root) + os.pathsep + os.environ["PATH"], "PYTHONDONTWRITEBYTECODE": "1"}
        success = self.root / "success"
        result = subprocess.run((*args, "--output", str(success)), env=environment, capture_output=True, text=True)
        self.assertEqual(result.returncode, 0, result.stdout + result.stderr)
        self.assertIn(f"{required_count}/{required_count} required checks passed", result.stdout)
        self.assertIn(f"✅ **{required_count}/{required_count}", (success / "summary.md").read_text())
        environment["FAIL_FIXTURE"] = "yes"
        failure = self.root / "failure"
        result = subprocess.run((*args, "--output", str(failure)), env=environment, capture_output=True, text=True)
        self.assertEqual(result.returncode, 1, result.stdout + result.stderr)
        self.assertIn(f"{required_count - 3}/{required_count} required checks passed", result.stdout)
        for name in ("frontend-build", "backend-build", "frontend-lint"):
            self.assertIn(f"❌ {name}", result.stdout)
        self.assertIn("✅ frontend-format", result.stdout)
        self.assertIn("backend-build (failed; exit 9)", (failure / "report.md").read_text())
        self.assertFalse((failure / "publication.json").exists())


if __name__ == "__main__":
    unittest.main()
