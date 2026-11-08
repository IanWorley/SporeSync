from __future__ import annotations

import contextlib
import io
import json
from pathlib import Path
import subprocess
import tempfile
import unittest
from unittest.mock import patch

import check_disclosure as policy


FIXTURE_PR_NUMBER = 52
SHA_LENGTH = 40


class DisclosureTests(unittest.TestCase):
    def test_full_agent_disclosure_is_valid(self) -> None:
        self.assertEqual(policy.validate(policy.DISCLOSURE), [])

    def test_generic_type_in_commit_subject_preserves_disclosure(self) -> None:
        self.assertEqual(policy.validate("feat(types): support List<String>\n\n" + policy.DISCLOSURE), [])

    def test_markdown_autolinks_preserve_disclosure(self) -> None:
        for link in ("<https://github.com/IanWorley/SporeSync>", "<contributor@example.test>"):
            with self.subTest(link=link):
                self.assertEqual(policy.validate(link + "\n\n" + policy.DISCLOSURE), [])

    def test_unknown_html_element_with_attributes_cannot_hide_disclosure(self) -> None:
        for attributes in ("hidden", 'style="display:none"'):
            with self.subTest(attributes=attributes):
                self.assertIn("missing field SporeSync-Authorship", policy.validate(f"<custom-element {attributes}>\n{policy.DISCLOSURE}\n</custom-element>"))

    def test_each_required_field_has_an_exact_missing_field_diagnostic(self) -> None:
        for field in policy.FIELDS:
            with self.subTest(field=field):
                text = "\n".join(line for line in policy.DISCLOSURE.splitlines() if not line.startswith(f"SporeSync-{field}:"))
                self.assertIn(f"missing field SporeSync-{field}", policy.validate(text))

    def test_empty_values_are_missing_fields(self) -> None:
        for field in policy.FIELDS:
            with self.subTest(field=field):
                text = "\n".join(f"SporeSync-{field}:   " if line.startswith(f"SporeSync-{field}:") else line for line in policy.DISCLOSURE.splitlines())
                self.assertIn(f"missing field SporeSync-{field}", policy.validate(text))

    def test_placeholders_and_malformed_values_fail(self) -> None:
        for field, value in (("Authorship", "agent"), ("Harness", "TODO"), ("Model", "<model>"),
                             ("Effort", "unspecified"), ("On-Behalf-Of", "IanWorley"), ("Harness", "T3 @unrelated")):
            with self.subTest(field=field, value=value):
                text = "\n".join(f"SporeSync-{field}: {value}" if line.startswith(f"SporeSync-{field}:") else line for line in policy.DISCLOSURE.splitlines())
                self.assertTrue(any(f"malformed field SporeSync-{field}" in error for error in policy.validate(text)))

    def test_human_authorship_has_a_truthful_alternative(self) -> None:
        self.assertEqual(policy.validate("A person wrote this.\nSporeSync-Authorship: human"), [])

    def test_human_authorship_cannot_mix_agent_metadata(self) -> None:
        self.assertIn("human declaration must not include agent metadata", policy.validate(policy.DISCLOSURE.replace(policy.AGENT, "human")))

    def test_undeclared_authorship_is_not_inferred_from_an_account(self) -> None:
        self.assertEqual(policy.validate("Written by @IanWorley"), ["missing field SporeSync-Authorship"])

    def test_duplicate_declarations_are_rejected(self) -> None:
        self.assertIn("duplicate field SporeSync-Authorship", policy.validate(policy.DISCLOSURE + "\nSporeSync-Authorship: human"))

    def test_markdown_cannot_hide_or_inject_disclosure_fields(self) -> None:
        self.assertTrue(policy.validate("> " + policy.DISCLOSURE.replace("\n", "\n> ")))
        self.assertTrue(policy.validate(policy.DISCLOSURE.replace("T3 Code / Codex", "[T3](https://evil.test)")))
        self.assertEqual(policy.validate("```text\n" + policy.DISCLOSURE + "\n```"), [])

    def test_hidden_html_declarations_do_not_satisfy_disclosure(self) -> None:
        for declaration in (policy.DISCLOSURE, "SporeSync-Authorship: human"):
            for opening, closing in (("<!--", "-->"), ("<details>", "</details>"), ("<details><details>", "</details></details>"), ("<span hidden>", "</span>"), ("<details></span>", "</details>")):
                with self.subTest(opening=opening, declaration=declaration):
                    self.assertIn("missing field SporeSync-Authorship", policy.validate(f"Visible text.\n{opening}\n{declaration}\n{closing}"))

    def test_harness_and_model_cannot_carry_mentions(self) -> None:
        for field in ("Harness", "Model"):
            text = "\n".join(f"SporeSync-{field}: @octocat" if line.startswith(f"SporeSync-{field}:") else line for line in policy.DISCLOSURE.splitlines())
            self.assertTrue(any(f"malformed field SporeSync-{field}" in error for error in policy.validate(text)))

    def test_every_commit_is_checked_and_new_commits_are_not_implicitly_exempt(self) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            for args in (("init", "-q"), ("config", "user.name", "Fixture"), ("config", "user.email", "fixture@example.test"),
                         ("commit", "--allow-empty", "-qm", "base")):
                subprocess.run(("git", *args), cwd=root, check=True, capture_output=True)
            base = subprocess.check_output(("git", "rev-parse", "HEAD"), cwd=root, text=True).strip()
            def commit(message: str) -> None:
                subprocess.run(("git", "commit", "--allow-empty", "-q", "-F", "-"), cwd=root, input=message, text=True, check=True)
            commit("feat(test): first\n\n" + policy.DISCLOSURE)
            commit("feat(test): missing disclosure")
            bad = subprocess.check_output(("git", "rev-parse", "HEAD"), cwd=root, text=True).strip()
            commit("feat(test): later\n\n" + policy.DISCLOSURE)
            with patch.object(policy, "ROOT", root), contextlib.redirect_stdout(io.StringIO()):
                self.assertEqual(policy.validate_commits(base, "HEAD"), [f"commit {bad}: missing field SporeSync-Authorship"])
                commit("feat(test): new undisclosed commit")
                self.assertEqual(len(policy.validate_commits(base, "HEAD")), 2)

    def test_issue_and_review_events_validate_only_the_event_text(self) -> None:
        for event_name, field in (("issues", "issue"), ("issue_comment", "comment"),
                                  ("pull_request_review", "review"), ("pull_request_review_comment", "comment")):
            with self.subTest(event_name=event_name):
                event = {"repository": {"full_name": policy.REPOSITORY}, field: {"body": policy.DISCLOSURE}}
                self.assertEqual(policy.check_event(event, event_name), [])
                event[field]["body"] = "missing"
                self.assertTrue(policy.check_event(event, event_name))

    def test_foreign_event_repository_is_rejected(self) -> None:
        with self.assertRaisesRegex(ValueError, "repository"):
            policy.check_event({"repository": {"full_name": "other/repo"}}, "issues")

    def test_gh_branch_discovery_supplies_the_selector_required_with_repo(self) -> None:
        response = subprocess.CompletedProcess([], 0, stdout=json.dumps({"url": f"https://github.com/{policy.REPOSITORY}/pull/{FIXTURE_PR_NUMBER}", "body": policy.DISCLOSURE}), stderr="")
        with patch.object(policy, "git", return_value="feat/local-ci-verifier"), patch.object(policy.subprocess, "run", return_value=response) as run:
            self.assertEqual(policy.gh_pr(None)["body"], policy.DISCLOSURE)
        self.assertEqual(run.call_args.args[0][:4], ["gh", "pr", "view", "feat/local-ci-verifier"])

    def test_pr_event_checks_body_and_all_commits_without_shell_interpolation(self) -> None:
        base, head = "a" * SHA_LENGTH, "b" * SHA_LENGTH
        event = {"repository": {"full_name": policy.REPOSITORY}, "pull_request": {
            "number": FIXTURE_PR_NUMBER, "base": {"sha": base}, "head": {"sha": head}, "body": "$(touch should-not-exist)\n" + policy.DISCLOSURE}}
        with patch.object(policy.subprocess, "run") as run, patch.object(policy, "git", return_value=head), patch.object(policy, "validate_commits", return_value=[]) as commits:
            self.assertEqual(policy.check_event(event, "pull_request"), [])
            self.assertNotIn("shell", run.call_args.kwargs)
            self.assertEqual(run.call_args.args[0][-1], "refs/pull/52/head")
            commits.assert_called_once_with(base, head)


if __name__ == "__main__":
    unittest.main()
