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

CI runs the commands below as six checks (`.github/workflows/ci.yml`). Run the checks for each side you change
before you push.

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

The "Module rules" section of `docs/SPEC.md` says which modules may depend on which.

## Conventions

- Use named constants, not magic values. detekt's `MagicNumber` rule fails the lint for `src/main`. It skips test
  sources, but the tests use named constants too, as in `FileStateStoreTest`.
- detekt runs its default rule set. The repo has no detekt config file.
- Kotlin code follows `ktlint_official` style at 120 columns (`backend/.editorconfig`).
- The frontend uses strict TypeScript (`frontend/tsconfig.json`). `vp lint` fails on the Oxlint `correctness`,
  `suspicious`, and `pedantic` categories, on `any`, and on non-null assertions (`frontend/vite.config.ts`).
- PR titles follow Conventional Commits, for example `feat(filestate): persist downloaded/extracted markers on disk`.
  The squash merge uses the title as the commit subject.

## Verify in the running app

Unit tests do not prove that the running app works. To prove backend behavior in the running app, use the project
skill in `.agents/skills/verify-sporesync`. Read its `SKILL.md` before you start an instance.
