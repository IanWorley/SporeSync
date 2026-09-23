# Kotlin/Spring backend build

## Build toolchain

The checked-in Gradle 9.7.1 wrapper builds the backend. It uses an included
Elide Gradle plugin, Kotlin, Spring Boot, and SpotBugs.

| Component | Version |
|-----------|---------|
| Gradle | 9.7.1 |
| Elide Gradle plugin source | `a1bb1307203acb44fa0d622aad4870c69f1144e1` |
| Managed Elide runtime | 1.5.3+20260917 |
| Java for the Elide plugin | 17 |
| Java for the backend | 25 |
| SpotBugs | 4.10.4 |
| Kotlin | 2.4.20 |
| Spring Boot | 4.1.1 |
| Spring Modulith | 2.1.1 |
| Embedded Tomcat | 11.0.26 |
| Jackson Kotlin module | 3.1.5 |
| PostgreSQL JDBC | 42.7.13 |
| Liquibase (through the Spring starter) | 5.0.3 |
| SLF4J API | 2.0.18 |
| Testcontainers | 2.0.5 |
| SSHJ | 0.40.0 |
| JNA | 5.18.1 |
| PostgreSQL test image | postgres:17.6-alpine |

`scripts/prepare-elide-plugin.sh` downloads the Elide Gradle plugin source at
the listed commit and verifies its pinned SHA-256 before Gradle includes it.
The plugin settings select the managed Elide runtime. Gradle downloads that
runtime and verifies the release's published SHA-256. The wrapper archive also
has a pinned SHA-256. Gradle owns dependency resolution through the Spring Boot
and Kotlin BOMs, with selected versions recorded in `gradle.lockfile`.
Build and configuration caching are enabled in `gradle.properties`.
After changing dependencies, run `./gradlew build --write-locks` and review the
lockfile diff. A populated dependency and runtime cache supports `--offline`.

The pinned plugin uses deprecated Gradle APIs. Keep Gradle below 10 until the
upstream plugin supports that version.

## Commands and scope

Run these commands **from `backend/`**:

```bash
bash scripts/prepare-elide-plugin.sh
./gradlew build
```

Install Java 17 and Temurin Java 25. `JAVA_HOME` must identify Java 25 because
it launches the Gradle daemon and the managed runtime. Gradle finds Java 17 in
its usual installation locations. For a Java 17 installation outside those
locations, set `JDK17` and run:

```bash
./gradlew -Porg.gradle.java.installations.paths="$JDK17,$JAVA_HOME" build
```

`./gradlew build` runs tests, `spotbugsMain`, and `elideCheckFormat`. `ModularityTest`
checks the five closed Spring Modulith packages and rejects forbidden dependencies.
Each module also has an isolated `@ApplicationModuleTest`. See
[backend modules](backend-modules.md) for ownership and focused test commands. Tests
require Docker for PostgreSQL and SSH Testcontainers.

Run a focused check when needed:

```bash
./gradlew spotbugsMain
./gradlew elideCheckFormat
./gradlew elideFormat
```

SpotBugs analyzes production classes only. `spotbugsTest` is disabled. SpotBugs
compares medium and high findings with `config/spotbugs-baseline.xml`, which
records 31 reviewed findings. Review each baseline update before committing it.
SpotBugs writes `build/reports/spotbugs/main.xml` and
`build/reports/spotbugs/main.html`. Gradle writes test reports to
`build/reports/tests/test`.

Liquibase creates `sporesync_settings` and `download_jobs`; Hibernate validates
the mapped schema. SSHJ supplies inventory and SFTP connections. JDBC stores
durable job state. Transfers, scheduling, and settings APIs are implemented.
Application login remains deferred.

For local development, run `./gradlew bootRun`. Run `./gradlew classes` after
editing Kotlin so DevTools can reload the compiled classes. To build the backend
JAR, run `./gradlew bootJar`, then start
`java -jar build/libs/sporesync.jar`. Spring reads
`config/application.properties` and the Liquibase changelog from the backend
working directory. Keep the frontend output in `../frontend/dist/` or set
`SPORESYNC_STATIC_LOCATION`.

For deployment, `scripts/package.sh` builds the backend JAR and packages it with
configuration, scanner, frontend assets, and `scripts/start.sh`. The target host
needs Java 25. It does not need Gradle or an Elide installation. See
[deployment](deployment.md).

## Frontend assets

Vite builds the React/TypeScript frontend separately with `npm run build` from
`frontend/`. Spring reads its output through `spring.web.resources.static-locations`,
defaulting to `file:../frontend/dist/`. Set `SPORESYNC_STATIC_LOCATION` to a directory
URL ending in `/` for a different deployment layout. The build keeps frontend
assets external rather than embedding them in the backend JAR.
The frontend uses relative `/api` URLs in both modes. Vite proxies those paths in
development; Spring handles them directly when serving the production bundle.
See the [README](../README.md) for startup and verification commands.

## Gradle migration validation

The Gradle build passed 74 tests with no failures or skips: 69 backend
integration tests, three SSH settings tests, and two download request tests.
Formatting passed for 33 Kotlin sources. SpotBugs accepted the 29 reviewed
baseline findings with no new findings, analysis errors, or missing classes.
All nine scanner tests passed with the real SSH fixture enabled.
`scripts/package.sh` built the frontend and JAR. The extracted archive's
`scripts/start.sh` served `/api/status` and the exact frontend HTML and JavaScript
against disposable PostgreSQL. The JAR excluded DevTools.
`python3 scripts/verify-runtime.py` passed automatic discovery, SFTP content,
cancellation, retry, process kill/restart, durable progress, and resumed checksum
verification using Java 25 and disposable PostgreSQL and SSH containers.
Earlier migration checks also verified offline builds, configuration-cache reuse,
and rejection of an injected null dereference by the Gradle SpotBugs task.
These checks ran on macOS arm64. Linux verification runs in CI.

## Download storage and verification

Transfer storage uses JNA POSIX calls on Linux and macOS to retain directory/file
descriptors through creation, transfer and publication. The metadata paths
`/proc/self/fd` (Linux) and `/dev/fd` (macOS) must be available. JNA loads its
platform native library from the resolved dependency. Windows downloads are not
supported. The integration suite exercises parent replacement, hard-link
preflight failures, resume and content replacement; run the Gradle checks
on macOS and the GitHub Linux runner when changing this native boundary.

Remote verification uses the seedbox's existing Python 3 installation over SSH.
Two server-side SHA-256 passes preserve content checks without fetching the whole
file twice over SFTP; SFTP uses bounded read-ahead for the missing suffix.
