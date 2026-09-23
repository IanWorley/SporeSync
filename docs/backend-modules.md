# Backend modules

Spring Modulith checks five closed modules under `dev.sporesync`. A module's root
package contains its public contracts. Its `internal` package contains controllers,
persistence, workers, and adapters. The application entry point and status endpoint
remain in `dev.sporesync`.

| Module | Owns | Public contracts | Allowed dependencies |
|--------|------|------------------|----------------------|
| `settings` | Persisted configuration and settings HTTP API | `DownloadSettings`, `DownloadSettingsStore`, `SshConnectionSettings` | None |
| `ssh` | External credentials, trust configuration, authentication selection | `SshSettings`, `SshAuthentication` | None |
| `inventory` | Remote scanning and inventory protocol validation | `InventoryScanner`, inventory values and failures | `settings`, `ssh` |
| `downloads` | Durable jobs, transfers, recovery, file actions, download HTTP API | `DownloadQueue`, `DownloadSpec` | `settings`, `ssh`, `inventory` |
| `discovery` | Scheduled scans, consecutive observations, automatic selection, inventory HTTP API | HTTP endpoints only | `settings`, `inventory`, `downloads` |

## Scanning and discovery have different responsibilities

`InventoryScanner.scan(connection)` requires an immutable connection snapshot.
The caller reads settings and selects that snapshot. The scanner does not read the
settings database. Manual download requests persist the same settings used to scan
for the requested file.

Discovery owns the rule that automatic downloads require two unchanged scans.
It passes eligible files to `DownloadQueue.acceptStable(spec)`. Downloads owns
deduplication and the decision to requeue a completed job when its local file is
missing. Discovery cannot access the download repository or filesystem storage.

The queue call is synchronous. A queue failure reaches the scan request before
discovery updates its successful snapshot. A failed scan clears consecutive-file
history, while retaining the last successful inventory for the dashboard.

The previous `sync` module hid scanning and download dependencies inside one
package. Splitting discovery from inventory removes that cycle. Direct calls keep
failure ordering explicit. An event dispatcher would add another delivery contract
for a single required consumer.

## Module boundaries are executable rules

Each `package-info.java` declares its allowed dependencies. All modules are closed.
`ModularityTest` verifies the graph and checks that implementation types stay
internal. Three test-only dependency probes confirm that the actual declarations
reject an unapproved dependency, access to download internals, and a cycle.
Normal module discovery excludes these probes.

Each module has an `@ApplicationModuleTest` that boots it independently. Tests
replace external collaborators through public contracts and use disposable
PostgreSQL where persistence is involved. Discovery tests cover stability,
configuration changes, safe errors, and synchronous queue failure. Download tests
cover durable deduplication, missing-file reconciliation, and existing stored JSON.
The full integration suite continues to exercise HTTP, real SSH and SFTP, filesystem
confinement, and worker recovery.
`scripts/verify-runtime.py` also waits for scheduled discovery to queue a file
without a manual scan, then checks pause, resume, and recovery after process termination.

Run the focused checks from `backend/`:

```sh
./gradlew test --tests '*ModuleTest' --tests '*ModularityTest'
```

`./gradlew build` includes these tests. The unchanged `DownloadSpec` JSON property
names and identity hash preserve existing `download_jobs.spec` rows. The literal
`legacy-download-spec.json` test fixture checks this compatibility.
