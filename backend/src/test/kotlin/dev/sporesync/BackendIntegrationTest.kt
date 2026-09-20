package dev.sporesync

import java.net.URI
import java.net.http.HttpClient
import java.net.http.HttpRequest
import java.net.http.HttpResponse
import java.nio.file.Files
import java.nio.file.Path
import java.security.KeyPairGenerator
import java.time.Duration
import java.util.Base64
import javax.sql.DataSource
import net.schmizz.sshj.common.Buffer
import org.junit.jupiter.api.AfterAll
import org.junit.jupiter.api.Assertions.assertEquals
import org.junit.jupiter.api.Assertions.assertThrows
import org.junit.jupiter.api.Assertions.assertTimeout
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
  @Autowired private lateinit var settings: SshSettings
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
    settings.host = ssh.host
    settings.port = ssh.getMappedPort(SSH_PORT)
    settings.username = "scanner"
    settings.privateKey = temporary.resolve("key").toString()
    settings.knownHosts = temporary.resolve("known_hosts").toString()
    settings.source = SPECIAL_SOURCE
    settings.scanner = "../scanner/inventory.py"
    settings.timeoutMillis = SSH_TIMEOUT_MILLIS
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
    settings.source = "/restricted"
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
    settings.knownHosts = temporary.resolve("untrusted").toString()
    Files.writeString(Path.of(settings.knownHosts), "")
    assertEquals(
        InventoryFailure.CONNECTION,
        assertThrows(InventoryException::class.java) { inventory.scan() }.code,
    )
  }

  @Test
  fun `reports authentication failures`() {
    settings.username = "nonexistent"
    assertEquals(
        InventoryFailure.AUTHENTICATION,
        assertThrows(InventoryException::class.java) { inventory.scan() }.code,
    )
  }

  @Test
  fun `uploads changed scanner and rejects unsupported inventory versions`() {
    val changed = temporary.resolve("changed.py")
    Files.writeString(changed, "print('{\"schemaVersion\":2,\"entries\":[]}')\n")
    settings.scanner = changed.toString()
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
    settings.scanner = slow.toString()
    settings.timeoutMillis = SHORT_TIMEOUT_MILLIS
    assertTimeout(HTTP_TIMEOUT) {
      assertEquals(
          InventoryFailure.TIMEOUT,
          assertThrows(InventoryException::class.java) { inventory.scan() }.code,
      )
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
