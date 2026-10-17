package com.sporesync

import org.junit.jupiter.api.Assertions.assertEquals
import org.junit.jupiter.api.Test
import org.springframework.boot.test.context.SpringBootTest
import org.springframework.boot.test.context.SpringBootTest.WebEnvironment
import org.springframework.boot.test.web.server.LocalServerPort
import tools.jackson.databind.json.JsonMapper
import java.net.URI
import java.net.http.HttpClient
import java.net.http.HttpRequest
import java.net.http.HttpResponse

private const val HEALTH_PATH = "/actuator/health"
private const val STATUS_FIELD = "status"
private const val STATUS_UP = "UP"
private const val HTTP_OK = 200

@SpringBootTest(webEnvironment = WebEnvironment.RANDOM_PORT)
class HealthEndpointTest(
    @LocalServerPort private val port: Int,
) {
    @Test
    fun `health endpoint reports UP`() {
        val request = HttpRequest.newBuilder(URI.create("http://localhost:$port$HEALTH_PATH")).build()

        val response = HttpClient.newHttpClient().send(request, HttpResponse.BodyHandlers.ofString())
        val status =
            JsonMapper
                .shared()
                .readTree(response.body())
                .get(STATUS_FIELD)
                .asString()

        assertEquals(HTTP_OK, response.statusCode())
        assertEquals(STATUS_UP, status)
    }
}
