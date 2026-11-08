# AGENTS.md

SporeSync mirrors files from a remote seedbox to a local machine over SSH, with a web UI.
[README.md](README.md) covers setup, layout, and every command. [docs/SPEC.md](docs/SPEC.md) is the target design.
Much of the spec is not built yet. Where the spec and the code disagree on tools, such as Prettier, ESLint, or Vite
on Elide, the code and this file are correct.

## Toolchain

- The backend in `backend/` is Kotlin, Spring Boot, and Spring Modulith. Elide 1.6.0 builds it, pinned in
  `.elideversion`. The repo has no Gradle or Maven. Dependencies and scripts live in `elide.pkl` files.
- The root `elide.pkl` is the Elide workspace. Its members are `backend/` and `tools/`. `tools/` resolves the ktlint
  and detekt jars.
- The frontend in `frontend/` is TypeScript and React. Elide 1.6.0 cannot run Vite or Vitest, so the frontend uses
  Vite+ (`vp`) on Node, with bun as the package manager. It is not an Elide workspace member.

## Run the CI checks

All contributors, especially agents, must run the local verification entrypoint before opening a PR:

```bash
python3 scripts/verify_ci.py --username IanWorley --harness "T3 Code / Codex" --model gpt-6.1-sol --effort high
```

Supply your own username, harness, model, and effort truthfully. These values are attribution, not verified identity.
Rerun after every source or HEAD change. Include the generated `.verification/<run>/summary.md` in the PR body.
Never claim readiness when required checks fail, time out, are unavailable, or the report is stale.
[CONTRIBUTING.md](CONTRIBUTING.md) documents prerequisites, all flags, the CI mapping, and approved comment publication.
Use `--pr NUMBER` to preview the same summary for the PR and its GitHub-linked closing issues.
Preview the exact `comment.md` and `destinations.json` to Ian before sending any PR or issue comments.
Only after approval, use `--publish RUN_DIR --pr NUMBER --post-comment --approve-summary SHA256`.
Without linked issues, report issue comments as not applicable. Never create an issue to satisfy verification.

The authoritative commands below are mapped from `.github/workflows/ci.yml`. The runner also checks its own tests
and fails closed when workflow files change until the command map is reviewed.

From the repository root:

```bash
elide install   # once per checkout, and after a dependency change
elide build
elide test
```

From `backend/`:

```bash
elide run format-check   # ktlint. Fix with `elide run format`.
elide run lint           # detekt
```

From `frontend/`:

```bash
vp install
vp test run
vp fmt --check   # Oxfmt. Fix with `vp fmt`.
vp lint          # Oxlint and a full TypeScript type-check
vp build         # CI's backend-test check runs this before `elide build`
```

## Backend modules

Each top-level package under `com.sporesync` is one Spring Modulith module. Kotlin has no `package-info`, so each
module has a marker class annotated `@PackageInfo` and `@ApplicationModule`, such as `delete/DeleteModule.kt`.

`ModularityTest` checks the module boundaries in `elide test`:

- `modules.verify()` fails if a module references a type in another module's subpackage. Only the root package of a
  module is its API. Any subpackage is internal, whatever its name. It also fails on a dependency cycle between
  modules.
- Another test compares the modules that Modulith finds against `SPEC_MODULES`. To add a module, create the package
  and its marker class, then add the module name to `SPEC_MODULES`.

Put a module's implementation types in its `internal` subpackage and mark them with Kotlin `internal` visibility, as
`backend/src/main/kotlin/com/sporesync/config/internal` does.

The "Module rules" section of `docs/SPEC.md` says which modules may depend on which.

## Persistence schema

Liquibase owns the database schema, and Hibernate only validates it (`ddl-auto: validate` in
`backend/src/main/resources/application.yaml`). If you add or change a JPA `@Entity`, add a changeset file under
`backend/src/main/resources/db/changelog/changes/`, and add an `include` entry for it to `db.changelog-master.yaml`.
The master file has no `includeAll`, so Liquibase runs only the files it names. If the schema does not match the
entities, the app fails at startup.

## Disclose GitHub authorship

Any agent-authored GitHub-facing text must explicitly identify itself as written by an agent, not a person.
This includes PR descriptions, issue posts, review comments, and commit messages.
Include the harness, effort, model, and human on whose behalf the agent speaks. Use this exact schema with truthful values:

```text
SporeSync-Authorship: agent (not a person)
SporeSync-Harness: T3 Code / Codex
SporeSync-Effort: high
SporeSync-Model: gpt-6.1-sol
SporeSync-On-Behalf-Of: @IanWorley
```

Human-authored text uses `SporeSync-Authorship: human` without agent metadata. Never label agent-authored text as human.
The checker validates declarations. It cannot infer reliable agent identity from a GitHub account or detect undeclared authorship.
All newly checked PR bodies and commits must declare human or agent authorship. Every new PR commit is checked, not just HEAD.
Include the disclosure below the Conventional Commit subject. Do not inject disclosure banners into application or code source files.
The local verifier and `.github/workflows/disclosure.yml` enforce the declarations. See CONTRIBUTING.md for event coverage and limitations.
Only commits `9b377cd7b53d115ff97b1974afbdad283c7d5a3f` and `20cafe87ffdbe6c17a01ed76f60579a58bca7d74`
are exempt because they were pushed in PR #52 before this policy was requested. They are not claimed as compliant or rewritten.
No later commit is exempt. Do not expand this compatibility baseline to avoid disclosures.

## Conventions

- Use named constants, not magic values. detekt's `MagicNumber` rule fails the lint for `src/main`, and Oxlint's
  `no-magic-numbers` rule fails `vp lint` outside `*.test.ts` and `*.test.tsx` files. The tests use named constants
  too, as in `FileStateStoreTest`.
- detekt runs its default rule set. The repo has no detekt config file.
- Kotlin code follows `ktlint_official` style at 120 columns (`backend/.editorconfig`).
- The frontend uses strict TypeScript (`frontend/tsconfig.json`). `vp lint` fails on the Oxlint `correctness`,
  `suspicious`, and `pedantic` categories, on `any`, on non-null assertions, and on the other rules in
  `frontend/vite.config.ts`.
- PR titles and commit subjects follow Conventional Commits, for example
  `feat(filestate): persist downloaded/extracted markers on disk`. A squash merge of a one-commit PR takes the
  commit's subject, not the PR title.

## Verify in the running app

Unit tests do not prove that the running app works. To prove backend behavior in the running app, use the project
skill in `.agents/skills/verify-sporesync`. Read its `SKILL.md` before you start an instance. Claude Code loads the same
skill as `/verify-sporesync` through the `.claude/skills` symlink.
