package dev.sporesync

import dev.sporesync.settings.ApplicationSetting
import dev.sporesync.settings.ApplicationSettingRepository
import dev.sporesync.settings.ApplicationSettings
import dev.sporesync.settings.SettingKey
import java.net.URI
import java.net.http.HttpClient
import java.net.http.HttpRequest
import java.net.http.HttpResponse
import java.nio.file.Files
import java.nio.file.Path
import java.security.KeyPairGenerator
import java.time.Duration
import java.time.Instant
import java.util.Base64
import javax.sql.DataSource
import liquibase.integration.spring.SpringLiquibase
import net.schmizz.sshj.common.Buffer
import org.junit.jupiter.api.AfterAll
import org.junit.jupiter.api.Assertions.assertEquals
import org.junit.jupiter.api.Assertions.assertNotNull
import org.junit.jupiter.api.Assertions.assertNull
import org.junit.jupiter.api.Assertions.assertThrows
import org.junit.jupiter.api.Assertions.assertTimeout
import org.junit.jupiter.api.Assertions.assertTrue
import org.junit.jupiter.api.BeforeAll
import org.junit.jupiter.api.BeforeEach
import org.junit.jupiter.api.Test
import org.junit.jupiter.api.TestInstance
import org.junit.jupiter.api.io.TempDir
import org.junit.jupiter.params.ParameterizedTest
import org.junit.jupiter.params.provider.ValueSource
import org.springframework.beans.factory.annotation.Autowired
import org.springframework.boot.test.context.SpringBootTest
import org.springframework.boot.test.web.server.LocalServerPort
import org.springframework.boot.testcontainers.service.connection.ServiceConnection
import org.springframework.test.context.DynamicPropertyRegistry
import org.springframework.test.context.DynamicPropertySource
import org.testcontainers.containers.GenericContainer
import org.testcontainers.containers.wait.strategy.Wait
import org.testcontainers.images.builder.ImageFromDockerfile
import org.testcontainers.junit.jupiter.Container
import org.testcontainers.junit.jupiter.Testcontainers
import org.testcontainers.postgresql.PostgreSQLContainer
import tools.jackson.databind.json.JsonMapper

@Testcontainers
@TestInstance(TestInstance.Lifecycle.PER_CLASS)
@SpringBootTest(
    webEnvironment = SpringBootTest.WebEnvironment.RANDOM_PORT,
    properties = ["sporesync.background.enabled=false"],
)
class BackendIntegrationTest {
  @LocalServerPort private var port: Int = 0
  @Autowired private lateinit var dataSource: DataSource
  @Autowired private lateinit var jsonMapper: JsonMapper
  @Autowired private lateinit var sshSettings: SshSettings
  @Autowired private lateinit var worker: DownloadWorker
  @Autowired private lateinit var downloader: SftpDownload
  @Autowired private lateinit var configuration: DownloadConfiguration
  @Autowired private lateinit var jobs: DownloadJobs
  @Autowired private lateinit var inventory: RemoteInventory
  private lateinit var temporary: Path
  private lateinit var ssh: GenericContainer<*>

  @BeforeAll
  fun startSsh(@TempDir directory: Path) {
    temporary = directory
    val key = temporary.resolve("key")
    val pair =
        KeyPairGenerator.getInstance("RSA").apply { initialize(TEST_KEY_BITS) }.generateKeyPair()
    val encoded =
        Base64.getMimeEncoder(PEM_LINE_WIDTH, "\n".toByteArray())
            .encodeToString(pair.private.encoded)
    Files.writeString(key, "-----BEGIN PRIVATE KEY-----\n$encoded\n-----END PRIVATE KEY-----\n")
    val publicKey =
        Base64.getEncoder()
            .encodeToString(Buffer.PlainBuffer().putPublicKey(pair.public).compactData)
    Files.writeString(temporary.resolve("key.pub"), "ssh-rsa $publicKey\n")
    ssh =
        GenericContainer(
                ImageFromDockerfile()
                    .withFileFromPath("Dockerfile", Path.of("../tests/ssh/Dockerfile"))
                    .withFileFromPath("authorized_keys", temporary.resolve("key.pub"))
            )
            .withExposedPorts(SSH_PORT)
            .waitingFor(Wait.forListeningPort())
    ssh.start()
    val hostKey =
        ssh.execInContainer("cat", "/etc/ssh/ssh_host_ed25519_key.pub").stdout.trim().split(" ")
    Files.writeString(
        temporary.resolve("known_hosts"),
        "[${ssh.host}]:${ssh.getMappedPort(SSH_PORT)} ${hostKey.take(2).joinToString(" ")}\n",
    )
    // Source arguments must survive shell metacharacters as well as Unicode.
    assertEquals(0, ssh.execInContainer("cp", "-a", "/seed", SPECIAL_SOURCE).exitCode)
  }

  @AfterAll
  fun stopSsh() {
    try {
      if (::ssh.isInitialized) ssh.close()
    } finally {
      Files.walk(staticDirectory).use { paths ->
        paths.sorted(Comparator.reverseOrder()).forEach(Files::delete)
      }
    }
  }

  @BeforeEach
  fun configureSsh() {
    settings.set(SshSettingKeys.HOST, ssh.host)
    settings.set(SshSettingKeys.PORT, ssh.getMappedPort(SSH_PORT))
    settings.set(SshSettingKeys.USERNAME, "scanner")
    sshSettings.privateKey = temporary.resolve("key").toString()
    sshSettings.knownHosts = temporary.resolve("known_hosts").toString()
    settings.set(SshSettingKeys.SOURCE, SPECIAL_SOURCE)
    sshSettings.scanner = "../scanner/inventory.py"
    settings.set(SshSettingKeys.TIMEOUT_MILLIS, SSH_TIMEOUT_MILLIS)
  }

  @Test
  fun `returns remote inventory over HTTP and reuses uploaded scanner`() {
    val request =
        HttpRequest.newBuilder(URI("http://127.0.0.1:$port/api/inventory/scan"))
            .timeout(HTTP_TIMEOUT)
            .POST(HttpRequest.BodyPublishers.noBody())
            .build()
    val response = HttpClient.newHttpClient().send(request, HttpResponse.BodyHandlers.ofString())
    assertEquals(HTTP_OK, response.statusCode(), response.body())
    val result = jsonMapper.readValue(response.body(), Inventory::class.java)
    val entries = result.entries.associateBy { it.path }
    assertEquals(1, result.schemaVersion)
    assertEquals(EntryType.directory, entries.getValue("empty").type)
    val file = entries.getValue("nested/日本語 file.txt")
    assertEquals(SAMPLE_BYTES, file.sizeBytes)
    val modified =
        ssh.execInContainer(
                "python3",
                "-c",
                "import os; print(os.stat(\"$SPECIAL_SOURCE/nested/日本語 file.txt\").st_mtime_ns)",
            )
            .stdout
            .trim()
            .toLong()
    assertEquals(modified, file.modifiedTimeNs)
    val cached =
        ssh.execInContainer("sh", "-c", "stat -c '%n %i %y' /home/scanner/.sporesync/*.py").stdout
    assertEquals(result, inventory.scan())
    assertEquals(
        cached,
        ssh.execInContainer("sh", "-c", "stat -c '%n %i %y' /home/scanner/.sporesync/*.py").stdout,
    )
  }

  @Test
  fun `uses updated source on next scan and keeps remote errors private`() {
    inventory.scan()
    settings.set(SshSettingKeys.SOURCE, "/restricted")
    val request =
        HttpRequest.newBuilder(URI("http://127.0.0.1:$port/api/inventory/scan"))
            .timeout(HTTP_TIMEOUT)
            .POST(HttpRequest.BodyPublishers.noBody())
            .build()
    val response = HttpClient.newHttpClient().send(request, HttpResponse.BodyHandlers.ofString())
    assertEquals(BAD_GATEWAY, response.statusCode())
    assertEquals("{\"error\":\"EXECUTION\"}", response.body())
  }

  @Test
  fun `rejects untrusted host keys`() {
    sshSettings.knownHosts = temporary.resolve("untrusted").toString()
    Files.writeString(Path.of(sshSettings.knownHosts), "")
    assertEquals(
        InventoryFailure.CONNECTION,
        assertThrows(InventoryException::class.java) { inventory.scan() }.code,
    )
  }

  @Test
  fun `reports authentication failures`() {
    settings.set(SshSettingKeys.USERNAME, "nonexistent")
    assertEquals(
        InventoryFailure.AUTHENTICATION,
        assertThrows(InventoryException::class.java) { inventory.scan() }.code,
    )
  }

  @Test
  fun `uploads changed scanner and rejects unsupported inventory versions`() {
    val changed = temporary.resolve("changed.py")
    Files.writeString(changed, "print('{\"schemaVersion\":2,\"entries\":[]}')\n")
    sshSettings.scanner = changed.toString()
    assertEquals(
        InventoryFailure.PROTOCOL,
        assertThrows(InventoryException::class.java) { inventory.scan() }.code,
    )
  }

  @Test
  fun `rejects incomplete file metadata`() {
    assertThrows(InventoryException::class.java) {
      inventory.decode("""{"schemaVersion":1,"entries":[{"path":"file","type":"file"}]}""")
    }
  }

  @Test
  fun `bounds remote execution time`() {
    val slow = temporary.resolve("slow.py")
    Files.writeString(slow, "import time\ntime.sleep(${HTTP_TIMEOUT.seconds})\n")
    sshSettings.scanner = slow.toString()
    settings.set(SshSettingKeys.TIMEOUT_MILLIS, SHORT_TIMEOUT_MILLIS)
    assertTimeout(HTTP_TIMEOUT) {
      assertEquals(
          InventoryFailure.TIMEOUT,
          assertThrows(InventoryException::class.java) { inventory.scan() }.code,
      )
    }
  }

  @Autowired private lateinit var settings: ApplicationSettings
  @Autowired private lateinit var settingsRepository: ApplicationSettingRepository

  @ParameterizedTest
  @ValueSource(strings = ["0", "65536", "invalid"])
  fun `rejects invalid database ports before connecting`(value: String) {
    settingsRepository.saveAndFlush(ApplicationSetting(SshSettingKeys.PORT.name, value))
    assertEquals(
        InventoryFailure.CONFIGURATION,
        assertThrows(InventoryException::class.java) { inventory.scan() }.code,
    )
  }

  @Test
  fun `requires a configured source instead of inventing a default`() {
    settingsRepository.deleteById(SshSettingKeys.SOURCE.name)
    assertEquals(
        InventoryFailure.CONFIGURATION,
        assertThrows(InventoryException::class.java) { inventory.scan() }.code,
    )
  }

  @ParameterizedTest
  @ValueSource(booleans = [false, true])
  fun `migration seeds defaults while preserving existing settings`(existing: Boolean) {
    val schema = if (existing) "ssh_existing" else "ssh_defaults"
    dataSource.connection.use {
      it.createStatement().use { statement -> statement.execute("CREATE SCHEMA $schema") }
    }
    fun migrate(file: String) {
      SpringLiquibase()
          .apply {
            dataSource = this@BackendIntegrationTest.dataSource
            defaultSchema = schema
            liquibaseSchema = schema
            changeLog = "file:./config/db/changes/$file.yaml"
          }
          .afterPropertiesSet()
    }
    migrate("001-create-settings")
    migrate("002-settings-timestamps")
    if (existing) {
      dataSource.connection.use { connection ->
        connection
            .prepareStatement("INSERT INTO $schema.sporesync_settings (name, value) VALUES (?, ?)")
            .use {
              it.setString(1, SshSettingKeys.PORT.name)
              it.setString(2, CUSTOM_SSH_PORT.toString())
              it.executeUpdate()
              it.setString(1, SshSettingKeys.SOURCE.name)
              it.setString(2, SPECIAL_SOURCE)
              it.executeUpdate()
            }
      }
    }
    migrate("003-ssh-defaults")
    migrate("004-ssh-setup-values")
    dataSource.connection.use { connection ->
      connection.createStatement().use { statement ->
        statement
            .executeQuery(
                "SELECT name, value, created_at, updated_at FROM $schema.sporesync_settings"
            )
            .use { rows ->
              val values = mutableMapOf<String, String>()
              while (rows.next()) {
                values[rows.getString("name")] = rows.getString("value")
                assertNotNull(rows.getTimestamp("created_at"))
                assertNotNull(rows.getTimestamp("updated_at"))
              }
              assertEquals(
                  mapOf(
                      SshSettingKeys.PORT.name to
                          (if (existing) CUSTOM_SSH_PORT else SSH_PORT).toString(),
                      SshSettingKeys.TIMEOUT_MILLIS.name to DEFAULT_SSH_TIMEOUT_MILLIS.toString(),
                      SshSettingKeys.HOST.name to "",
                      SshSettingKeys.USERNAME.name to "",
                      SshSettingKeys.SOURCE.name to (if (existing) SPECIAL_SOURCE else ""),
                  ),
                  values,
              )
            }
      }
    }
  }

  @Test
  fun `serves typed application status over HTTP`() {
    val request =
        HttpRequest.newBuilder(URI("http://127.0.0.1:$port/api/status"))
            .timeout(HTTP_TIMEOUT)
            .GET()
            .build()
    val response = HttpClient.newHttpClient().send(request, HttpResponse.BodyHandlers.ofString())

    assertEquals(HTTP_OK, response.statusCode())
    assertEquals(
        ApplicationStatus("sporesync"),
        jsonMapper.readValue(response.body(), ApplicationStatus::class.java),
    )
  }

  @Test
  fun `initializes Liquibase against PostgreSQL`() {
    dataSource.connection.use { connection ->
      connection.createStatement().use { statement ->
        statement.executeQuery("SELECT COUNT(*) FROM databasechangeloglock").use { rows ->
          rows.next()
          assertEquals(SINGLE_LOCK_ROW, rows.getInt(1))
        }
      }
    }
  }

  @Test
  fun `persists strings without changing their contents`() {
    val key = SettingKey("test.path", { it }, { value: String -> value })
    val path = "/downloads/日本語 files"

    settings.set(key, path)

    assertEquals(path, settings.get(key))
    assertEquals(path, settingsRepository.findById(key.name).orElseThrow().value)
  }

  @Test
  fun `converts a stored string to its declared type`() {
    val key = SettingKey("test.interval", Duration::parse, Duration::toString)
    val interval = Duration.ofMinutes(5)

    settings.set(key, interval)

    assertEquals("PT5M", settingsRepository.findById(key.name).orElseThrow().value)
    assertEquals(interval, settings.get(key))
  }

  @Test
  fun `updates the value for an existing name`() {
    val key = SettingKey("test.enabled", String::toBooleanStrict, Boolean::toString)
    settings.set(key, false)

    settings.set(key, true)

    assertEquals(true, settings.get(key))
  }

  @Test
  fun `assigns timestamps when creating a setting`() {
    val key = SettingKey("test.timestamps.create", String::toBooleanStrict, Boolean::toString)

    settings.set(key, true)

    val stored = settingsRepository.findById(key.name).orElseThrow()
    assertNotNull(stored.createdAt)
    assertNotNull(stored.updatedAt)
    assertTrue(!stored.updatedAt.isBefore(stored.createdAt))
  }

  @Test
  fun `updates modification time while preserving creation time`() {
    val key = SettingKey("test.timestamps.update", String::toBooleanStrict, Boolean::toString)
    val originalTime = Instant.parse("2020-01-01T00:00:00Z")
    // Seed an older row so timestamp advancement does not depend on sleeps or clock resolution.
    dataSource.connection.use { connection ->
      connection
          .prepareStatement(
              "INSERT INTO sporesync_settings (name, value, created_at, updated_at) VALUES (?, ?, ?, ?)"
          )
          .use { statement ->
            statement.setString(1, key.name)
            statement.setString(2, "false")
            statement.setObject(3, originalTime.atOffset(java.time.ZoneOffset.UTC))
            statement.setObject(4, originalTime.atOffset(java.time.ZoneOffset.UTC))
            statement.executeUpdate()
          }
    }

    settings.set(key, true)

    val stored = settingsRepository.findById(key.name).orElseThrow()
    assertEquals(originalTime, stored.createdAt)
    assertTrue(stored.updatedAt.isAfter(originalTime))
    assertEquals(true, settings.get(key))
  }

  @Test
  fun `returns null for a missing setting`() {
    val key = SettingKey("test.missing", String::toInt, Int::toString)

    assertNull(settings.get(key))
  }

  @Test
  fun `rejects malformed values instead of applying an implicit default`() {
    val key = SettingKey("test.invalid", String::toBooleanStrict, Boolean::toString)
    settingsRepository.saveAndFlush(ApplicationSetting(key.name, "not-a-boolean"))

    assertThrows(IllegalArgumentException::class.java) { settings.get(key) }
  }

  @Test
  fun `serves frontend entry point from external static directory`() {
    val response = get("/")
    assertEquals(HTTP_OK, response.statusCode())
    assertEquals(INDEX_HTML, response.body())
  }

  @Test
  fun `serves built assets from external static directory`() {
    val response = get("/assets/app.js")
    assertEquals(HTTP_OK, response.statusCode())
    assertEquals(ASSET_JS, response.body())
  }

  @Test
  fun `unknown API routes remain not found`() {
    assertEquals(HTTP_NOT_FOUND, get("/api/missing").statusCode())
  }

  @Test
  fun `missing assets remain not found`() {
    assertEquals(HTTP_NOT_FOUND, get("/assets/missing.js").statusCode())
  }

  @Test
  fun `settings API saves a complete form and never exposes credentials`() {
    val value =
        DownloadSettings(
            "seed.example",
            username = "scanner",
            source = "/seed",
            destination = "/downloads",
        )
    val request =
        HttpRequest.newBuilder(URI("http://127.0.0.1:$port/api/settings"))
            .header("Content-Type", "application/json")
            .PUT(HttpRequest.BodyPublishers.ofString(jsonMapper.writeValueAsString(value)))
            .build()
    val response = HttpClient.newHttpClient().send(request, HttpResponse.BodyHandlers.ofString())
    assertEquals(HTTP_OK, response.statusCode())
    val loaded = get("/api/settings")
    assertEquals(value, jsonMapper.readValue(loaded.body(), DownloadSettings::class.java))
    assertTrue(!loaded.body().contains("privateKey") && !loaded.body().contains("passphrase"))
  }

  @Test
  fun `invalid settings cannot partially update persisted values`() {
    val before = get("/api/settings").body()
    val value =
        DownloadSettings(
            "changed.example",
            username = "scanner",
            source = "/seed",
            destination = "relative",
        )
    val request =
        HttpRequest.newBuilder(URI("http://127.0.0.1:$port/api/settings"))
            .header("Content-Type", "application/json")
            .PUT(HttpRequest.BodyPublishers.ofString(jsonMapper.writeValueAsString(value)))
            .build()
    assertEquals(
        HTTP_BAD_REQUEST,
        HttpClient.newHttpClient().send(request, HttpResponse.BodyHandlers.ofString()).statusCode(),
    )
    assertEquals(before, get("/api/settings").body())
  }

  @Test
  fun `queue persists snapshots and deduplicates repeated inventory`() {
    val spec = jobSpec("deduplicated.txt")
    val job = jobs.enqueue(spec)
    jobs.enqueue(spec)
    assertEquals(1, jobs.list().count { it.id == job.id })
    assertEquals(spec, jobs.find(job.id)?.spec)
  }

  @Test
  fun `interrupted work recovers with progress and bounded attempts`() {
    val job = jobs.enqueue(jobSpec("interrupted.txt"))
    repeat(MAX_DOWNLOAD_ATTEMPTS) { attempt ->
      jobs.start(job.id)
      jobs.progress(job.id, SAMPLE_BYTES)
      jobs.recover()
      val recovered = requireNotNull(jobs.find(job.id))
      assertEquals(SAMPLE_BYTES, recovered.bytesDone)
      assertEquals(attempt + 1, recovered.attempts)
    }
    assertEquals(JobState.FAILED, jobs.find(job.id)?.state)
    assertEquals(JobState.QUEUED, jobs.retry(job.id)?.state)
    assertEquals(0, jobs.find(job.id)?.attempts)
  }

  @Test
  fun `cancellation survives recovery and requires explicit retry`() {
    val job = jobs.enqueue(jobSpec("cancelled.txt"))
    jobs.start(job.id)
    jobs.cancel(job.id)
    jobs.recover()
    assertEquals(JobState.CANCELLED, jobs.find(job.id)?.state)
    jobs.enqueue(job.spec)
    assertEquals(JobState.CANCELLED, jobs.find(job.id)?.state)
    assertEquals(JobState.QUEUED, jobs.retry(job.id)?.state)
  }

  @ParameterizedTest
  @ValueSource(booleans = [true, false])
  fun `downloads Unicode nested file over real SFTP in both filename modes`(
      temporaryMode: Boolean
  ) {
    val spec = transferSpec(temporaryMode)
    val progress = mutableListOf<Long>()
    downloader.transfer(spec, progress::add) { false }
    val target = Path.of(spec.settings.destination).resolve(spec.entry.path)
    assertEquals("sample\n", Files.readString(target))
    assertEquals(SAMPLE_BYTES, progress.last())
  }

  @Test
  fun `resumes existing final file only after verifying its prefix`() {
    val spec = transferSpec(true)
    val target = Path.of(spec.settings.destination).resolve(spec.entry.path)
    Files.createDirectories(target.parent)
    Files.writeString(target, "sam")
    downloader.transfer(spec, {}) { false }
    assertEquals("sample\n", Files.readString(target))
    downloader.transfer(spec, {}) { false }
    assertEquals("sample\n", Files.readString(target))
  }

  @Test
  fun `mismatched local prefix fails without modifying the file`() {
    val spec = transferSpec(false)
    val target = Path.of(spec.settings.destination).resolve(spec.entry.path)
    Files.createDirectories(target.parent)
    Files.writeString(target, "bad")
    assertEquals(
        "LOCAL_CONFLICT",
        assertThrows(DownloadFailure::class.java) {
              downloader.transfer(spec, {}) { false }
            }
            .code,
    )
    assertEquals("bad", Files.readString(target))
  }

  @Test
  fun `cancelled temporary download resumes retained partial data`() {
    val spec = transferSpec(true)
    var cancel = false
    assertThrows(DownloadFailure::class.java) {
      downloader.transfer(spec, { cancel = true }) { cancel }
    }
    val target = Path.of(spec.settings.destination).resolve(spec.entry.path)
    assertTrue(!Files.exists(target))
    downloader.transfer(spec, {}) { false }
    assertEquals("sample\n", Files.readString(target))
  }

  @Test
  fun `destination rejects traversal and symlink parents`(@TempDir root: Path) {
    assertThrows(IllegalArgumentException::class.java) { downloader.safeTarget(root, "../escape") }
    val outside = Files.createTempDirectory(temporary, "outside")
    Files.createSymbolicLink(root.resolve("link"), outside)
    assertThrows(IllegalArgumentException::class.java) { downloader.safeTarget(root, "link/file") }
    assertTrue(!Files.exists(outside.resolve("file")))
  }

  @Test
  fun `HTTP queued transfer completes in worker after request finishes`() {
    jobs.list().forEach { jobs.cancel(it.id) }
    val spec = transferSpec(true)
    configuration.save(spec.settings)
    val request =
        HttpRequest.newBuilder(URI("http://127.0.0.1:$port/api/downloads"))
            .header("Content-Type", "application/json")
            .POST(
                HttpRequest.BodyPublishers.ofString(
                    jsonMapper.writeValueAsString(DownloadRequest(spec.entry.path))
                )
            )
            .build()
    val response = HttpClient.newHttpClient().send(request, HttpResponse.BodyHandlers.ofString())
    assertEquals(202, response.statusCode(), response.body())
    val job = jsonMapper.readValue(response.body(), DownloadJob::class.java)
    assertEquals(JobState.QUEUED, job.state)
    worker.tick()
    assertEquals(JobState.COMPLETE, jobs.find(job.id)?.state)
    assertEquals(SAMPLE_BYTES, jobs.find(job.id)?.bytesDone)
    assertEquals(
        "sample\n",
        Files.readString(Path.of(spec.settings.destination).resolve(spec.entry.path)),
    )
  }

  @Test
  fun `worker recovers persisted running job with a fresh worker instance`() {
    jobs.list().forEach { jobs.cancel(it.id) }
    val job = jobs.enqueue(transferSpec(true))
    jobs.start(job.id)
    DownloadWorker(dataSource, jobs, downloader, false).tick()
    assertEquals(JobState.COMPLETE, jobs.find(job.id)?.state)
    assertEquals(2, jobs.find(job.id)?.attempts)
  }

  @Test
  fun `second worker cannot claim a job while advisory lock is owned`() {
    jobs.list().forEach { jobs.cancel(it.id) }
    val job = jobs.enqueue(transferSpec(false))
    dataSource.connection.use { connection ->
      connection.createStatement().use { statement ->
        statement.execute("SELECT pg_advisory_lock(1397772114)")
        try {
          DownloadWorker(dataSource, jobs, downloader, false).tick()
          assertEquals(JobState.QUEUED, jobs.find(job.id)?.state)
        } finally {
          statement.execute("SELECT pg_advisory_unlock(1397772114)")
        }
      }
    }
  }

  @Test
  fun `automatic queue waits for two stable scans and deduplicates later scans`() {
    val spec = transferSpec(true)
    val discovery = Discovery(inventory, configuration, jobs, false)
    val snapshot = Inventory(1, listOf(spec.entry))
    discovery.accept(spec.settings.copy(automatic = true), snapshot)
    assertNull(jobs.find(spec.identity()))
    discovery.accept(spec.settings.copy(automatic = true), snapshot)
    assertEquals(JobState.QUEUED, jobs.find(spec.identity())?.state)
    discovery.accept(spec.settings.copy(automatic = true), snapshot)
    assertEquals(1, jobs.list().count { it.id == spec.identity() })
  }

  @Test
  fun `changing file is ineligible until a later stable scan`() {
    val spec = transferSpec(true)
    val discovery = Discovery(inventory, configuration, jobs, false)
    discovery.accept(
        spec.settings.copy(automatic = true),
        Inventory(1, listOf(spec.entry.copy(sizeBytes = 1))),
    )
    discovery.accept(spec.settings.copy(automatic = true), Inventory(1, listOf(spec.entry)))
    assertNull(jobs.find(spec.identity()))
    discovery.accept(spec.settings.copy(automatic = true), Inventory(1, listOf(spec.entry)))
    assertNotNull(jobs.find(spec.identity()))
  }

  @Test
  fun `disabled automatic downloads still publish discovery without queuing`() {
    val spec = transferSpec(true)
    val discovery = Discovery(inventory, configuration, jobs, false)
    val snapshot = Inventory(1, listOf(spec.entry))
    repeat(2) { discovery.accept(spec.settings.copy(automatic = false), snapshot) }
    assertNull(jobs.find(spec.identity()))
    assertEquals(snapshot, discovery.state().inventory)
  }

  private fun transferSpec(temporaryMode: Boolean): DownloadSpec {
    val destination = Files.createTempDirectory(temporary, "downloads").toRealPath().toString()
    val settings =
        configuration.read().copy(destination = destination, temporaryFiles = temporaryMode)
    val entry = inventory.scan().entries.single { it.path == "nested/日本語 file.txt" }
    return DownloadSpec(settings, entry)
  }

  private fun jobSpec(path: String) =
      DownloadSpec(
          DownloadSettings(
              "seed.example",
              username = "scanner",
              source = "/seed",
              destination = "/downloads",
          ),
          InventoryEntry(path, EntryType.file, SAMPLE_BYTES, 0),
      )

  private fun get(path: String): HttpResponse<String> {
    val request =
        HttpRequest.newBuilder(URI("http://127.0.0.1:$port$path"))
            .timeout(HTTP_TIMEOUT)
            .GET()
            .build()
    return HttpClient.newHttpClient().send(request, HttpResponse.BodyHandlers.ofString())
  }

  companion object {
    private const val SSH_PORT = 22
    private const val CUSTOM_SSH_PORT = 2222
    private const val DEFAULT_SSH_TIMEOUT_MILLIS = 30000
    private const val TEST_KEY_BITS = 2048
    private const val PEM_LINE_WIDTH = 64
    private const val SSH_TIMEOUT_MILLIS = 5000
    private const val SHORT_TIMEOUT_MILLIS = 1000
    private const val SAMPLE_BYTES = 7L
    private const val BAD_GATEWAY = 502
    private const val SPECIAL_SOURCE = "/seed 日本語 ' ; literal"
    private const val POSTGRES_IMAGE = "postgres:17.6-alpine"
    private const val HTTP_BAD_REQUEST = 400
    private const val HTTP_OK = 200
    private const val HTTP_NOT_FOUND = 404
    private const val INDEX_HTML = "<!doctype html><title>SporeSync static fixture</title>"
    private const val ASSET_JS = "console.info('static asset fixture');"
    private const val SINGLE_LOCK_ROW = 1
    private val HTTP_TIMEOUT = Duration.ofSeconds(10)

    @Container @ServiceConnection @JvmField val postgres = PostgreSQLContainer(POSTGRES_IMAGE)

    // Spring creates the per-class context before JUnit initializes @TempDir fields.
    private val staticDirectory = Files.createTempDirectory("sporesync-static-test")

    @JvmStatic
    @DynamicPropertySource
    fun staticResources(registry: DynamicPropertyRegistry) {
      val directory = staticDirectory
      Files.writeString(directory.resolve("index.html"), INDEX_HTML)
      Files.createDirectories(directory.resolve("assets"))
      Files.writeString(directory.resolve("assets/app.js"), ASSET_JS)
      registry.add("SPORESYNC_STATIC_LOCATION") { directory.toUri().toString() }
    }
  }
}
