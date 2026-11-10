# Verify before opening a pull request

All contributors, especially agents, must run this from the repository root before opening a PR:

```bash
python3 scripts/verify_ci.py --username IanWorley --harness "T3 Code / Codex" --model gpt-6.1-sol --effort high
```

Supply your own GitHub username without `@`, harness, model, and effort truthfully.
For example, an Opus 5.5 caller can supply `--harness "T3 Code / Claude" --model "Opus 5.5" --effort high`.
These strings are attribution, not verified identity. No model SDK or model authentication is required.

Install Python 3.11 or newer, Elide from `.elideversion`, and Vite+ with the bun version from `frontend/package.json`.
Put `python3`, `elide`, `vp`, and `bun` on `PATH`. The runner installs project dependencies with the existing commands.
The runner attempts to stop timed-out process groups and bounds output draining. Detached daemons can survive; inspect timed-out logs and stop them separately. Run it on macOS or Linux.
This is local verification on your operating system, not proof of identical behavior on GitHub's Ubuntu runner.

The runner attempts every command after failures. Any failed, unavailable, or timed-out required check exits nonzero.
Workflow drift and observed source changes during execution also fail verification.
Source snapshots are compared before and after every command. Avoid concurrent edits.
Changes made and restored entirely between observations cannot be proven absent; these checks are not filesystem isolation.
Rerun after every source or HEAD change. Include `.verification/<run>/summary.md` in the PR body.
Never claim readiness while checks fail or evidence is stale. Keep `report.md`, `evidence.json`, and logs for review.
Reports record the starting HEAD, dirty state, toolchain versions, commands, outcomes, and durations.
Assume-unchanged and skip-worktree index flags make the source ineligible for publication.
Saved evidence, summaries, and logs have integrity hashes; modified or incomplete runs cannot be published.
These hashes detect changes, not a malicious local author who rewrites both evidence and hashes. Dirty runs cannot be published as comments.
Logs redact known secret environment values and common token forms. Review logs before sharing them.
Published summaries omit command output and evidence-directory paths. Do not put secrets in metadata or command arguments.

## Map local checks to CI

`scripts/verify_ci.py` contains the command map and SHA256 fingerprints of every workflow file.
When workflows change, inspect all jobs and steps, update the map and exclusions, then update the fingerprints.
The `ci-coverage` check fails if any workflow is added, removed, or changed without that review.
The runner delegates lint and format rules to the authoritative tools and configs.

| Runner check | CI job or setup | Command and directory |
|---|---|---|
| ci-coverage | Every workflow | Compare workflow inventory and fingerprints |
| python-version | Local prerequisite for verifier | `python3 --version` at root, requires Python 3.11+ |
| elide-version | setup-elide | `elide --version` at root, enforces `.elideversion` |
| vp-version | setup-vp | `vp --version` in `frontend/`, records Vite+ version |
| bun-version | setup-vp | `bun --version` in `frontend/`, enforces `packageManager` in `frontend/package.json` |
| runner-tests | verifier / runner-tests | `python3 -m unittest discover -s scripts -p 'test_*.py' -v` at root |
| frontend-install | setup-vp for frontend jobs and backend-test | `vp install` in `frontend/` |
| backend-install | All backend jobs | `elide install` at root |
| frontend-build | backend-test prerequisite | `vp build` in `frontend/` |
| backend-build | backend-test | `elide build` at root |
| backend-test | backend-test | `elide test` at root |
| backend-format | backend-format | `elide run format-check` in `backend/` |
| backend-lint | backend-lint | `elide run lint` in `backend/` |
| frontend-test | frontend-test | `vp test run` in `frontend/` |
| frontend-format | frontend-format | `vp fmt --check` in `frontend/` |
| frontend-lint | frontend-lint | `vp lint` in `frontend/`, including TypeScript checking |

Backend format uses ktlint_official and `backend/.editorconfig`. Backend lint uses detekt's default rules.
Frontend format uses Oxfmt. Frontend lint uses all enabled Oxlint rules and type checks in `frontend/vite.config.ts`.
Dependencies are installed once per run rather than once per matrix job.
The count measures command gates, not GitHub job counts. There are no app smoke checks in the current workflows.
GitHub provisioning, checkout, caches, and toolchain downloads are hosted setup, replaced by local prerequisites.
CodeRabbit and Cursor Security Agent run as hosted services. They are excluded and never counted as passing.

## Use the CLI flags

| Flag | Meaning |
|---|---|
| `--help` | Show executable usage |
| `--username NAME` | Attribution for the person on whose behalf verification runs |
| `--harness TEXT` | Caller-supplied harness attribution |
| `--model TEXT` | Caller-supplied model attribution |
| `--effort TEXT` | Caller-supplied effort attribution |
| `--timeout SECONDS` | Positive finite timeout per command, default 900 |
| `--output DIRECTORY` | New evidence directory, default `.verification/<UTC timestamp>` |
| `--pr NUMBER` | Resolve allowed PR and linked issue destinations and save previews without posting |
| `--publish RUN_DIR` | Reuse a saved run after approval, without rerunning checks |
| `--post-comment` | Explicitly publish the approved summary to every validated destination |
| `--approve-summary SHA256` | Digest of the exact approved `comment.md` preview |

Use a new output directory under `.verification/` or outside the repository so evidence does not dirty the source tree.
Existing output directories are never overwritten by a verification run.

## Preview and approve PR and issue comments

GitHub publication uses existing `gh` authentication and is restricted to `IanWorley/SporeSync`.
The PR must be open, its head must equal the clean verified SHA, and its source repository must be SporeSync.
Only GitHub's `closingIssuesReferences` relationship supplies issue destinations. Arbitrary references in PR prose are never parsed.
Each issue must belong to SporeSync and is validated through the issue API. Cross-repository targets fail closed.
If GitHub reports no linked closing issues, issue comments are explicitly not applicable. Do not create an issue for this report.

After the implementation is committed and its branch is pushed, add `--pr NUMBER` to the verification command.
Alternatively, resolve a saved run without running the checks again:

```bash
python3 scripts/verify_ci.py --publish .verification/RUN --pr NUMBER
```

Show Ian the exact `.verification/RUN/comment.md` and `.verification/RUN/destinations.json` before sending any comments.
The summary includes counts, exact failure names, commands, outcomes, attribution, and the source SHA.
It is identical on the PR and every linked issue. The destination list is part of that text and its approval SHA256.
The CLI prints the digest.
Only after Ian approves that text and those destinations, publish:

```bash
python3 scripts/verify_ci.py --publish .verification/RUN --pr NUMBER --post-comment --approve-summary APPROVED_SHA256
```

Publication revalidates the source, PR head, and issue relationships immediately before each write.
GitHub offers no atomic operation combining a head comparison and comment creation, so a final network race remains possible. Changed destinations require another preview and approval.
The runner does not edit comments. It reuses an identical comment by the authenticated author with the same PR, SHA, and report digest marker.
A new approved run on the same SHA can create a new summary; retries of that run reuse its existing comments.
A different existing summary with that marker fails rather than overwriting it. Concurrent publishers can still race.
Each destination's outcome and successful comment URL are saved in `publication.json`.
If one destination fails, the runner attempts the rest and exits nonzero. Retry the same approved run to avoid duplicate successes.
Failed verification can be published honestly with approval, but still exits nonzero and does not establish readiness.
