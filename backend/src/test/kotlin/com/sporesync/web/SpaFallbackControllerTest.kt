package com.sporesync.web

import org.junit.jupiter.api.Assertions.assertEquals
import org.junit.jupiter.api.Assertions.assertTrue
import org.junit.jupiter.api.Test
import org.springframework.boot.test.context.SpringBootTest
import org.springframework.boot.test.context.SpringBootTest.WebEnvironment
import org.springframework.boot.test.web.server.LocalServerPort
import tools.jackson.databind.json.JsonMapper
import java.net.URI
import java.net.http.HttpClient
import java.net.http.HttpRequest
import java.net.http.HttpResponse

private const val HTML_OK = 200
private const val NOT_FOUND = 404
private const val ROOT_MARKER = "SporeSync frontend shell"
private const val PATH_FIELD = "path"

@SpringBootTest(
    webEnvironment = WebEnvironment.RANDOM_PORT,
    properties = ["spring.web.resources.static-locations=classpath:/test-frontend/"],
)
class SpaFallbackControllerTest(
    @LocalServerPort private val port: Int,
) {
    @Test
    fun `client route falls back to index html`() {
        val response = get("/settings")

        assertEquals(HTML_OK, response.statusCode())
        assertTrue(response.headers().firstValue("content-type").orElse("").contains("text/html"))
        assertTrue(response.body().contains(ROOT_MARKER))
    }

    @Test
    fun `api routes stay on backend routing`() {
        val response = get("/api/status")
        val errorPath = JsonMapper.shared().readTree(response.body()).get(PATH_FIELD).asText()

        assertEquals(NOT_FOUND, response.statusCode())
        assertEquals("/api/status", errorPath)
    }

    private fun get(path: String): HttpResponse<String> {
        val request = HttpRequest.newBuilder(URI.create("http://localhost:$port$path")).build()
        return HttpClient.newHttpClient().send(request, HttpResponse.BodyHandlers.ofString())
    }
}
