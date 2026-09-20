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
import org.springframework.beans.factory.annotation.Autowired
import org.springframework.boot.test.context.SpringBootTest
import org.springframework.boot.test.web.server.LocalServerPort
import org.springframework.boot.testcontainers.service.connection.ServiceConnection
import org.testcontainers.containers.GenericContainer
import org.testcontainers.containers.wait.strategy.Wait
import org.testcontainers.images.builder.ImageFromDockerfile
import org.testcontainers.junit.jupiter.Container
import org.testcontainers.junit.jupiter.Testcontainers
import org.testcontainers.postgresql.PostgreSQLContainer
import tools.jackson.databind.json.JsonMapper

@Testcontainers
@TestInstance(TestInstance.Lifecycle.PER_CLASS)
@SpringBootTest(webEnvironment = SpringBootTest.WebEnvironment.RANDOM_PORT)
class BackendIntegrationTest {
  @LocalServerPort private var port: Int = 0
  @Autowired private lateinit var dataSource: DataSource
  @Autowired private lateinit var jsonMapper: JsonMapper
  @Autowired private lateinit var sshSettings: SshSettings
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
    if (::ssh.isInitialized) ssh.close()
  }

  @BeforeEach
  fun configureSsh() {
    sshSettings.host = ssh.host
    sshSettings.port = ssh.getMappedPort(SSH_PORT)
    sshSettings.username = "scanner"
    sshSettings.privateKey = temporary.resolve("key").toString()
    sshSettings.knownHosts = temporary.resolve("known_hosts").toString()
    sshSettings.source = SPECIAL_SOURCE
    sshSettings.scanner = "../scanner/inventory.py"
    sshSettings.timeoutMillis = SSH_TIMEOUT_MILLIS
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
  fun `reports inaccessible source without exposing remote details`() {
    sshSettings.source = "/restricted"
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
    sshSettings.username = "nonexistent"
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
    sshSettings.timeoutMillis = SHORT_TIMEOUT_MILLIS
    assertTimeout(HTTP_TIMEOUT) {
      assertEquals(
          InventoryFailure.TIMEOUT,
          assertThrows(InventoryException::class.java) { inventory.scan() }.code,
      )
    }
  }

  @Autowired private lateinit var settings: ApplicationSettings
  @Autowired private lateinit var settingsRepository: ApplicationSettingRepository

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

  companion object {
    private const val SSH_PORT = 22
    private const val TEST_KEY_BITS = 2048
    private const val PEM_LINE_WIDTH = 64
    private const val SSH_TIMEOUT_MILLIS = 5000
    private const val SHORT_TIMEOUT_MILLIS = 1000
    private const val SAMPLE_BYTES = 7L
    private const val BAD_GATEWAY = 502
    private const val SPECIAL_SOURCE = "/seed 日本語 ' ; literal"
    private const val POSTGRES_IMAGE = "postgres:17.6-alpine"
    private const val HTTP_OK = 200
    private const val SINGLE_LOCK_ROW = 1
    private val HTTP_TIMEOUT = Duration.ofSeconds(10)

    @Container @ServiceConnection @JvmField val postgres = PostgreSQLContainer(POSTGRES_IMAGE)
  }
}
