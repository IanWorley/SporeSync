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
| authorship-disclosure | disclosure / PR and new commit declarations | `python3 scripts/check_disclosure.py local` at root |
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
| `--pr-body PATH` | Draft PR description for disclosure validation before a PR exists |
| `--authorship agent` or `--authorship human` | Truthful authorship declaration for the generated report, default agent |
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

## Declare authorship in GitHub-facing text

Any agent-authored PR description, issue post, review comment, or commit message must say that an agent wrote it, not a person.
Include all five fields below. Change the metadata truthfully for each author. This run uses these values:

```text
SporeSync-Authorship: agent (not a person)
SporeSync-Harness: T3 Code / Codex
SporeSync-Effort: high
SporeSync-Model: gpt-6.1-sol
SporeSync-On-Behalf-Of: @IanWorley
```

Put the block in a PR or issue body, a review or conversation comment, or below a Conventional Commit subject.
Do not put author banners into application or code source files. Checker fixtures and documentation examples may contain the schema.
Only a person who wrote the text may use this alternative, with no agent metadata:

```text
SporeSync-Authorship: human
```

Every newly checked text must declare one of these alternatives. The checker does not infer authorship from account names.
An agent must never use the human alternative. Declarations are self-attested; the checker cannot prove who wrote text or detect a false human declaration.
Agent fields cannot be empty, duplicated, placeholders, markup, or control characters.
Declarations inside HTML comments or HTML containers, including collapsed details, do not count.
Put the disclosure in plain Markdown or a fenced text block outside HTML containers, preferably before HTML examples.
Harness and model use ASCII names containing letters, digits, spaces, periods, slashes, plus signs, underscores, parentheses, and hyphens; effort is `low`, `medium`, `high`, `xhigh`, `max`, or `ultra`, and the human field is `@GitHubUsername`.
Missing fields fail with exact diagnostics such as `missing field SporeSync-Harness`.

The local runner checks the draft or current PR description and every new commit since the merge base with `origin/main`.
For a new PR, write its description with the disclosure first, then run:

```bash
python3 scripts/verify_ci.py --username IanWorley --harness "T3 Code / Codex" --model gpt-6.1-sol --effort high --pr-body /tmp/sporesync-pr-body.md
```

Without `--pr-body`, existing `gh` authentication reads the current branch's PR body from SporeSync.
The body hash is recorded in the disclosure log. The generated `summary.md` includes its own complete disclosure.
Replace the draft's standalone disclosure block with the summary instead of appending a second declaration.
Keep the rest of the PR description. On reruns, replace the previous summary. The final body must contain exactly
one declaration. Use the same truthful authorship for the draft and report, with `--authorship human` for a human-written
report. Never relabel human-written prose as agent-authored or agent-written prose as human-authored.
After embedding the generated report in the PR body, rerun
`python3 scripts/check_disclosure.py local --pr NUMBER` to validate the final body without a self-referential report hash. This command reads GitHub; it does not create posts or comments.
Commit additional changes with their own full disclosures, then rerun. A compliant head commit does not excuse an earlier undisclosed commit.
Two exact historical commits in PR #52 are exempt because they were pushed before this requirement arrived:
`9b377cd7b53d115ff97b1974afbdad283c7d5a3f` and `20cafe87ffdbe6c17a01ed76f60579a58bca7d74`.
The checker reports them as `EXEMPT`, not disclosure passes. Their messages were not amended, rebased, or force-pushed.
There are no date, author-account, or future-commit exemptions.

## Understand disclosure check coverage

`.github/workflows/disclosure.yml` reports `authorship-disclosure` for PR events and `authorship-disclosure-event` for posted-text events.
The separate names prevent a passing comment check from replacing the PR body and commit verdict.

| Event | Activity types | Validated text |
|---|---|---|
| `pull_request` | opened, reopened, edited, synchronize, ready_for_review | Current PR body and every commit in the event base-to-head range |
| `issues` | opened, edited, reopened | Event issue body |
| `issue_comment` | created, edited | Event issue or PR conversation comment |
| `pull_request_review` | submitted, edited | Event review body, when it contains text |
| `pull_request_review_comment` | created, edited | Event inline review comment |

The existing CI workflow also reruns on those PR activity types. New commits rerun both workflows through `synchronize`.
The PR job fetches the event's PR head through a numeric pull ref and reads commit messages. It never executes code from those fetched commits.
The workflow uses ordinary `pull_request`, not `pull_request_target`, with read-only `contents` permission and no injected secrets.
Checkout does not persist credentials. Event text is read as JSON from `GITHUB_EVENT_PATH`, never interpolated into shell commands.
Ordinary PR CI runs the checked-out PR code with read-only permissions. Review workflow changes before merging them, as with every CI policy change.

The `issues` and `issue_comment` workflows require the file on the default branch, so those checks activate after merge.
The `pull_request`, `pull_request_review`, and `pull_request_review_comment` events use the PR merge ref.
PR changes can alter those workflow/checker files; review such changes before merging. Merge conflicts can prevent these PR event runs.
Each posted-text run covers its own event. A later event success does not clear another event's failure; inspect the individual Actions runs, since repeated event check names can visually supersede older results.
They validate only the event text, not all historical or unrelated repository text. Empty reviews with no authored text are explicitly not applicable.
External bots are not silently treated as humans; their text also needs a truthful declaration when these events are checked.
These read-only checks report failures. They cannot edit or prevent an already-posted issue or comment, enforce branch protection, or prove identity.
Make the PR check required through repository settings if merge blocking is desired. This change does not alter repository settings.
