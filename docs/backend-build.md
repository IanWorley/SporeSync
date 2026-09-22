# Kotlin/Spring backend build

## Verified toolchain

Verified on macOS arm64 on 2026-09-20 using Elide directly, without Maven or
Gradle build files or an independently installed JDK.

| Component | Version |
|-----------|---------|
| Elide | 1.5.3+20260917.e2442e4 |
| Bundled Java | 25.0.4.1; target 25 |
| Bundled Kotlin compiler and Kotlin reflection | 2.4.20 |
| Spring Boot | 4.1.1 |
| Jackson Kotlin module | 3.1.5 |
| PostgreSQL JDBC | 42.7.13 |
| Liquibase (through the Spring starter) | 5.0.3 |
| SLF4J API | 2.0.18 |
| Testcontainers | 2.0.5 |
| SSHJ | 0.40.0 |
| JNA | 5.18.1 |
| PostgreSQL test image | postgres:17.6-alpine |

The manifest names direct versions once. Jackson, JDBC, Testcontainers,
and SLF4J versions follow Spring Boot 4.1.1's dependency BOM; Kotlin follows Elide's
bundled compiler. Spring starters supply their transitive dependencies. This is
not a separately imported Boot BOM or a committed transitive dependency lock.
Review the resolved classpaths when changing versions. SLF4J is pinned directly
because adding JPA otherwise resolved its incompatible 1.7.36 API under Elide.

`backend/.elideversion` selects 1.5.3. This is a release-version selector, not an
exact nightly pin. The dated selector triggered repeated downloads in this
verification, while the full build identifier failed resolution. The exact
binary tested is available from the [20260917 release](https://github.com/elide-dev/elide/releases/tag/1.5.3%2B20260917).
The macOS arm64 archive's published SHA-256 was verified before execution:
`c342f4a4843a451a8bf98ed203f3b63c5b4684301e405551e2bfb1d73835895e`.

## Commands and scope

Run these commands **from `backend/`**:

```bash
elide install --slim
elide build
elide test
elide format -- -n src
```

`elide test` requires Docker and starts disposable PostgreSQL and SSH/Python
containers. It checks HTTP inventory, scanner caching, host-key verification,
authentication failures, execution timeouts, protocol validation, Liquibase,
typed settings persistence through Spring Data JPA, and SSH defaults migration
on fresh and previously configured databases.
Temporary SSH credentials are generated in Java; no local OpenSSH tool is needed
for backend tests. Containers and temporary keys are cleaned up after the tests.

Initial scaffold verification passed: `elide build`, `elide test` (2 passed, 0 skipped), and
`elide format -- -n src`. Both `elide run` and `elide run -fDEV` served the
expected response against disposable PostgreSQL; changing a compiled class
timestamp triggered a DevTools restart. Scanner/SSH tests were not rerun because
those files were unchanged. SSH inventory integration now passes 26 backend tests
with no skips, plus all 9 Python tests with the SSH fixture enabled.

Settings foundation verification passed: `elide build`, `elide test` (9 passed,
0 skipped against disposable PostgreSQL), and `elide format -- -n src`.

For normal startup, set `SPRING_DATASOURCE_URL`, `SPRING_DATASOURCE_USERNAME`,
and `SPRING_DATASOURCE_PASSWORD` for an existing PostgreSQL database, then run
`elide run`. The server defaults to loopback port 8080. The status endpoint only
identifies the running application; it does not report seedbox or transfer health.

`elide run -fDEV` includes DevTools; `elide build -fDEV` recompiles changes for
its restart watcher. Normal builds/runs exclude DevTools. LiveReload is disabled; Vite handles frontend hot updates. This does not provide a source compiler watcher.

## Elide constraints

- The embedded `elide:` manifest schema works with the verified binary. The
  documentation's versioned `pkl.elide.dev/1.5.3/project.pkl` URL returned 404.
- Source-set compilation did not copy resource files in the feasibility probe.
  Spring reads external `config/application.properties`, and Liquibase reads
  `config/db/changelog.yaml` relative to the working directory. Do not run from
  the repository root with only `-p backend`; that does not establish Spring's
  configuration working directory.
- `elide build` compiles classes. Executable JAR/container packaging is deferred;
  a deployment must eventually carry both configuration and dependencies.
- Kotlin configuration uses `proxyBeanMethods = false`, avoiding an all-open
  compiler plugin for the scaffold. Revisit proxy requirements when adding
  transactional services; do not assume final Kotlin classes can be proxied.

Liquibase creates `sporesync_settings`; Hibernate validates the schema rather
than creating or updating it. Spring Data JPA repositories supply transaction
boundaries for settings reads and writes. The entity has an explicit protected
no-argument constructor and open properties for JPA, without compiler plugins.
The settings service does not need transactional proxying for its single repository
calls. SSHJ supplies the inventory connection; transfer jobs, scheduling, settings
APIs, and application security are later slices.

Sources: [JVM workflow](https://elide.help/docs/jvm),
[manifest reference](https://elide.help/docs/elide-pkl-reference),
[build flags](https://elide.help/docs/tooling-build-flags), and
[Spring Boot dependency BOM](https://repo.maven.apache.org/maven2/org/springframework/boot/spring-boot-dependencies/4.1.1/spring-boot-dependencies-4.1.1.pom).

## Frontend assets

Vite builds the React/TypeScript frontend separately with `npm run build` from
`frontend/`. Spring reads its output through `spring.web.resources.static-locations`,
defaulting to `file:../frontend/dist/`. Set `SPORESYNC_STATIC_LOCATION` to a directory
URL ending in `/` for a different deployment layout. This avoids relying on Elide
resource copying; the build does not embed assets into compiled classes or a JAR.
The frontend uses relative `/api` URLs in both modes. Vite proxies those paths in
development; Spring handles them directly when serving the production bundle.
See the [README](../README.md) for startup and verification commands.

Frontend integration verification (2026-09-20): `npm ci` and `npm run build`
passed with Node 24.20.0; `elide build`, `elide test` (6 passed, 0 skipped), and
`elide format -- -n src` passed. A running backend with disposable PostgreSQL
served the actual Vite HTML and JavaScript without Vite running. Vite then
successfully proxied `/api/status` to a custom backend port via `BACKEND_URL`;
an unknown API path returned 404. The backend suite also verifies external
static fixtures and missing assets. Scanner/SSH files were unchanged.

## Download storage and verification

Transfer storage uses JNA POSIX calls on Linux and macOS to retain directory/file
descriptors through creation, transfer and publication. The metadata paths
`/proc/self/fd` (Linux) and `/dev/fd` (macOS) must be available. JNA loads its
platform native library from the resolved dependency. Windows downloads are not
supported. The integration suite exercises parent replacement, hard-link
preflight failures, resume and content replacement; run the existing Elide checks
on macOS and the GitHub Linux runner when changing this native boundary.

Remote verification uses the seedbox's existing Python 3 installation over SSH.
Two server-side SHA-256 passes preserve content checks without fetching the whole
file twice over SFTP; SFTP uses bounded read-ahead for the missing suffix.
