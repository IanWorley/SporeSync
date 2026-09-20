package dev.sporesync

import java.net.URI
import java.net.http.HttpClient
import java.net.http.HttpRequest
import java.net.http.HttpResponse
import java.time.Duration
import javax.sql.DataSource
import org.junit.jupiter.api.Assertions.assertEquals
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
    private const val POSTGRES_IMAGE = "postgres:17.6-alpine"
    private const val HTTP_OK = 200
    private const val SINGLE_LOCK_ROW = 1
    private val HTTP_TIMEOUT = Duration.ofSeconds(10)

    @Container @ServiceConnection @JvmField val postgres = PostgreSQLContainer(POSTGRES_IMAGE)
  }
}
