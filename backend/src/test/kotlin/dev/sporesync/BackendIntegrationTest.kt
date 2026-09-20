package dev.sporesync

import dev.sporesync.settings.ApplicationSetting
import dev.sporesync.settings.ApplicationSettingRepository
import dev.sporesync.settings.ApplicationSettings
import dev.sporesync.settings.SettingKey
import java.net.URI
import java.net.http.HttpClient
import java.net.http.HttpRequest
import java.net.http.HttpResponse
import java.time.Duration
import javax.sql.DataSource
import org.junit.jupiter.api.Assertions.assertEquals
import org.junit.jupiter.api.Assertions.assertNull
import org.junit.jupiter.api.Assertions.assertThrows
import org.junit.jupiter.api.Test
import org.springframework.beans.factory.annotation.Autowired
import org.springframework.boot.test.context.SpringBootTest
import org.springframework.boot.test.web.server.LocalServerPort
import org.springframework.boot.testcontainers.service.connection.ServiceConnection
import org.testcontainers.junit.jupiter.Container
import org.testcontainers.junit.jupiter.Testcontainers
import org.testcontainers.postgresql.PostgreSQLContainer
import tools.jackson.databind.json.JsonMapper

@Testcontainers
@SpringBootTest(webEnvironment = SpringBootTest.WebEnvironment.RANDOM_PORT)
class BackendIntegrationTest {
  @LocalServerPort private var port: Int = 0
  @Autowired private lateinit var dataSource: DataSource
  @Autowired private lateinit var jsonMapper: JsonMapper
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
    private const val POSTGRES_IMAGE = "postgres:17.6-alpine"
    private const val HTTP_OK = 200
    private const val SINGLE_LOCK_ROW = 1
    private val HTTP_TIMEOUT = Duration.ofSeconds(10)

    @Container @ServiceConnection @JvmField val postgres = PostgreSQLContainer(POSTGRES_IMAGE)
  }
}
