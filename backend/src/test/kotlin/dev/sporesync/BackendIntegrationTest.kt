package dev.sporesync

import com.sun.jna.Native
import dev.sporesync.config.SshSettingKeys
import dev.sporesync.config.SshSettings
import dev.sporesync.model.ApplicationStatus
import dev.sporesync.model.download.DownloadFailure
import dev.sporesync.model.download.DownloadJob
import dev.sporesync.model.download.DownloadJobs
import dev.sporesync.model.download.DownloadRequest
import dev.sporesync.model.download.DownloadSpec
import dev.sporesync.model.download.DownloadWorker
import dev.sporesync.model.download.JobState
import dev.sporesync.model.download.LibC
import dev.sporesync.model.download.MAX_DOWNLOAD_ATTEMPTS
import dev.sporesync.model.download.Posix
import dev.sporesync.model.download.PosixDownloadStorage
import dev.sporesync.model.download.SftpDownload
import dev.sporesync.model.download.WORKER_LOCK_ID
import dev.sporesync.model.inventory.EntryType
import dev.sporesync.model.inventory.Inventory
import dev.sporesync.model.inventory.InventoryEntry
import dev.sporesync.model.inventory.InventoryException
import dev.sporesync.model.inventory.InventoryFailure
import dev.sporesync.model.inventory.RemoteInventory
import dev.sporesync.model.settings.ApplicationSetting
import dev.sporesync.model.settings.ApplicationSettingRepository
import dev.sporesync.model.settings.ApplicationSettings
import dev.sporesync.model.settings.DownloadConfiguration
import dev.sporesync.model.settings.DownloadSettings
import dev.sporesync.model.settings.MAX_TIMEOUT_MILLIS
import dev.sporesync.model.settings.SettingKey
import java.net.URI
import java.net.http.HttpClient
import java.net.http.HttpRequest
import java.net.http.HttpResponse
import java.nio.ByteBuffer
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

  @ParameterizedTest
  @ValueSource(booleans = [false, true])
  fun `remote replacement during transfer prevents completion`(replaceByRename: Boolean) {
    val source = "/replacement-source"
    ssh.execInContainer("mkdir", "-p", source)
    ssh.execInContainer("sh", "-c", "printf original > $source/file")
    settings.set(SshSettingKeys.SOURCE, source)
    val destination = Files.createTempDirectory(temporary, "replacement").toRealPath().toString()
    val spec =
        DownloadSpec(
            configuration.read().copy(destination = destination, temporaryFiles = true),
            inventory.scan().entries.single(),
        )
    assertEquals(
        "REMOTE_CHANGED",
        assertThrows(DownloadFailure::class.java) {
              downloader.transfer(
                  spec,
                  {
                    val command =
                        if (replaceByRename)
                            "printf replaced > $source/new; touch -r $source/file $source/new; mv $source/new $source/file"
                        else "printf replaced > $source/file"
                    assertEquals(0, ssh.execInContainer("sh", "-c", command).exitCode)
                  },
              ) {
                false
              }
            }
            .code,
    )
    assertTrue(!Files.exists(Path.of(destination).resolve("file")))
  }

  @Test
  fun `remote growth resumes verified final content`() {
    val source = "/growth-source"
    ssh.execInContainer("mkdir", "-p", source)
    ssh.execInContainer("sh", "-c", "printf first > $source/file")
    settings.set(SshSettingKeys.SOURCE, source)
    val destination = Files.createTempDirectory(temporary, "growth").toRealPath().toString()
    val config = configuration.read().copy(destination = destination)
    val first = DownloadSpec(config, inventory.scan().entries.single())
    downloader.transfer(first, {}) { false }
    ssh.execInContainer("sh", "-c", "printf second >> $source/file")
    val grown = first.copy(entry = inventory.scan().entries.single())
    downloader.transfer(grown, {}) { false }
    assertEquals("firstsecond", Files.readString(Path.of(destination).resolve("file")))
  }

  @Test
  fun `smaller remote file never truncates a longer local file`() {
    val spec = transferSpec(false)
    val target = Path.of(spec.settings.destination).resolve(spec.entry.path)
    Files.createDirectories(target.parent)
    Files.writeString(target, "sample\nextra")
    assertEquals(
        "LOCAL_CONFLICT",
        assertThrows(DownloadFailure::class.java) {
              downloader.transfer(spec, {}) { false }
            }
            .code,
    )
    assertEquals("sample\nextra", Files.readString(target))
  }

  @Test
  fun `configured destination may use a filesystem alias`() {
    val spec = transferSpec(true)
    val alias = temporary.resolve("destination-alias")
    Files.createSymbolicLink(alias, Path.of(spec.settings.destination))
    downloader.transfer(
        spec.copy(settings = spec.settings.copy(destination = alias.toString())),
        {},
    ) {
      false
    }
    assertEquals("sample\n", Files.readString(alias.resolve(spec.entry.path)))
  }

  @Test
  fun `cancelled queued job cannot be started by a racing worker`() {
    val job = jobs.enqueue(jobSpec("cancel-before-start.txt"))
    jobs.cancel(job.id)
    assertEquals(false, jobs.start(job.id))
    assertEquals(JobState.CANCELLED, jobs.find(job.id)?.state)
  }

  @Test
  fun `download configuration preserves persisted SSH timeout`() {
    val configuration = DownloadConfiguration(settingsRepository)
    settings.set(SshSettingKeys.TIMEOUT_MILLIS, SHORT_TIMEOUT_MILLIS)
    val loaded = configuration.read()
    assertEquals(SHORT_TIMEOUT_MILLIS, loaded.timeoutMillis)
    configuration.save(loaded.copy(destination = temporary.toString()))
    assertEquals(SHORT_TIMEOUT_MILLIS, settings.get(SshSettingKeys.TIMEOUT_MILLIS))
    assertThrows(IllegalArgumentException::class.java) {
      configuration.save(
          loaded.copy(destination = temporary.toString(), timeoutMillis = MAX_TIMEOUT_MILLIS + 1)
      )
    }
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
  fun `overlapping transfer in the same JVM reports destination busy`() {
    val spec = transferSpec(true)
    var attempted = false
    downloader.transfer(
        spec,
        {
          if (!attempted) {
            attempted = true
            assertEquals(
                "DESTINATION_BUSY",
                assertThrows(DownloadFailure::class.java) {
                      downloader.transfer(spec, {}) { false }
                    }
                    .code,
            )
          }
        },
    ) {
      false
    }
    assertTrue(attempted)
  }

  @Test
  fun `publishing never replaces a destination created during transfer`(@TempDir root: Path) {
    PosixDownloadStorage().open(root.toString(), "file", true).use { download ->
      download.file.write(ByteBuffer.wrap("download".toByteArray()))
      val target = Files.writeString(root.resolve("file"), "existing data")
      assertEquals(
          "LOCAL_CONFLICT",
          assertThrows(DownloadFailure::class.java) { download.publish() }.code,
      )
      assertEquals("existing data", Files.readString(target))
      assertEquals("download".length.toLong(), download.file.size())
    }
  }

  @Test
  fun `destination rejects traversal and symlink parents`(@TempDir root: Path) {
    val storage = PosixDownloadStorage()
    assertThrows(IllegalArgumentException::class.java) {
      storage.open(root.toString(), "../escape", true)
    }
    val outside = Files.createTempDirectory(temporary, "outside")
    Files.createSymbolicLink(root.resolve("link"), outside)
    assertThrows(DownloadFailure::class.java) { storage.open(root.toString(), "link/file", true) }
    assertTrue(!Files.exists(outside.resolve("file")))
  }

  @ParameterizedTest
  @ValueSource(booleans = [true, false])
  fun `replaced parent cannot redirect writes or publication`(
      temporaryMode: Boolean,
      @TempDir root: Path,
  ) {
    val outside = Files.createTempDirectory(temporary, "outside")
    PosixDownloadStorage().open(root.toString(), "nested/file", temporaryMode).use { download ->
      Files.move(root.resolve("nested"), root.resolve("held"))
      Files.createSymbolicLink(root.resolve("nested"), outside)
      download.file.write(ByteBuffer.wrap("download".toByteArray()))
      download.publish()
      assertEquals("download", Files.readString(root.resolve("held/file")))
      assertTrue(!Files.exists(outside.resolve("file")))
    }
  }

  @Test
  fun `unsupported hard links fail before SSH authentication`(@TempDir root: Path) {
    val unsupportedOperation = 95 // Linux ENOTSUP; only the stable failure category is asserted.
    val real = Posix().libc
    val noLinks =
        object : LibC by real {
          override fun linkat(
              source: Int,
              name: String,
              destination: Int,
              target: String,
              flags: Int,
          ): Int {
            Native.setLastError(unsupportedOperation)
            return -1
          }
        }
    val downloader = SftpDownload(SshSettings(), PosixDownloadStorage(Posix(noLinks)))
    val spec =
        jobSpec("file").let { it.copy(settings = it.settings.copy(destination = root.toString())) }
    assertEquals(
        "UNSUPPORTED_DESTINATION",
        assertThrows(DownloadFailure::class.java) { downloader.transfer(spec, {}) { false } }.code,
    )
    assertTrue(!Files.exists(root.resolve("file")))
    Files.list(root.resolve(".sporesync")).use { paths ->
      assertTrue(paths.noneMatch { it.fileName.toString().startsWith(".sporesync-probe-") })
    }
  }

  @Test
  fun `creates a missing destination beneath a configured filesystem alias`(@TempDir root: Path) {
    val alias = root.resolve("alias")
    val real = Files.createDirectory(root.resolve("real"))
    Files.createSymbolicLink(alias, real)
    PosixDownloadStorage().open(alias.resolve("new/downloads").toString(), "file", true).use {
        download ->
      download.file.write(ByteBuffer.wrap("download".toByteArray()))
      download.publish()
    }
    assertEquals("download", Files.readString(real.resolve("new/downloads/file")))
  }

  @Test
  fun `published content preserves normal umask permissions`(@TempDir root: Path) {
    val referenceDirectory = Files.createDirectory(root.resolve("reference"))
    val referenceFile = Files.createFile(referenceDirectory.resolve("file"))
    PosixDownloadStorage().open(root.toString(), "nested/file", true).use { download ->
      download.publish()
    }
    assertEquals(
        Files.getPosixFilePermissions(referenceDirectory),
        Files.getPosixFilePermissions(root.resolve("nested")),
    )
    assertEquals(
        Files.getPosixFilePermissions(referenceFile),
        Files.getPosixFilePermissions(root.resolve("nested/file")),
    )
    assertEquals(
        java.nio.file.attribute.PosixFilePermissions.fromString("rwx------"),
        Files.getPosixFilePermissions(root.resolve(".sporesync")),
    )
  }

  @Test
  fun `failed publication sync retains staging and reports failure`(@TempDir root: Path) {
    var failSync = false
    val real = Posix().libc
    val failingSync =
        object : LibC by real {
          override fun fsync(descriptor: Int): Int = if (failSync) -1 else real.fsync(descriptor)
        }
    PosixDownloadStorage(Posix(failingSync)).open(root.toString(), "file", true).use { download ->
      download.file.write(ByteBuffer.wrap("download".toByteArray()))
      download.file.force()
      failSync = true
      assertEquals(
          "LOCAL_IO_FAILED",
          assertThrows(DownloadFailure::class.java) { download.publish() }.code,
      )
      Files.list(root.resolve(".sporesync")).use { paths ->
        assertTrue(paths.anyMatch { it.fileName.toString().endsWith(".part") })
      }
    }
  }

  @Test
  fun `resume transfers the suffix after a large verified prefix`() {
    val source = "/resume-source"
    val prefixBytes = 128 * 1024
    val suffixBytes = 96 * 1024
    assertEquals(0, ssh.execInContainer("mkdir", "-p", source).exitCode)
    assertEquals(
        0,
        ssh.execInContainer(
                "python3",
                "-c",
                "from pathlib import Path; Path('$source/file').write_bytes(b'a' * $prefixBytes + b'b' * $suffixBytes)",
            )
            .exitCode,
    )
    settings.set(SshSettingKeys.SOURCE, source)
    val destination = Files.createTempDirectory(temporary, "resume")
    val spec =
        DownloadSpec(
            configuration.read().copy(destination = destination.toString(), temporaryFiles = false),
            inventory.scan().entries.single(),
        )
    val target = destination.resolve("file")
    val prefix = "a".repeat(prefixBytes)
    Files.writeString(target, prefix)
    val progress = mutableListOf<Long>()
    downloader.transfer(spec, progress::add) { false }
    assertTrue(progress.isNotEmpty() && progress.all { it > prefixBytes })
    assertEquals(prefix + "b".repeat(suffixBytes), Files.readString(target))
  }

  @Test
  fun `connection failures stop retrying after the configured attempt limit`() {
    jobs.list().forEach { jobs.cancel(it.id) }
    val spec = transferSpec(false)
    java.net.ServerSocket(0).use { unavailable ->
      val job = jobs.enqueue(spec.copy(settings = spec.settings.copy(port = unavailable.localPort)))
      // A listening non-SSH socket times out; use its port after closure for immediate refusal.
      unavailable.close()
      repeat(MAX_DOWNLOAD_ATTEMPTS) { worker.tick() }
      assertEquals(JobState.FAILED, jobs.find(job.id)?.state)
      assertEquals(MAX_DOWNLOAD_ATTEMPTS, jobs.find(job.id)?.attempts)
    }
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
    assertEquals(HTTP_ACCEPTED, response.statusCode(), response.body())
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
        statement.execute("SELECT pg_advisory_lock($WORKER_LOCK_ID)")
        try {
          DownloadWorker(dataSource, jobs, downloader, false).tick()
          assertEquals(JobState.QUEUED, jobs.find(job.id)?.state)
        } finally {
          statement.execute("SELECT pg_advisory_unlock($WORKER_LOCK_ID)")
        }
      }
    }
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
    private const val HTTP_ACCEPTED = 202
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
