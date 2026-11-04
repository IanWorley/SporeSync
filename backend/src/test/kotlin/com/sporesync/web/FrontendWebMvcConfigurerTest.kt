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
private const val STATUS_FIELD = "status"
private const val STATUS_UP = "UP"
private const val PATH_FIELD = "path"

@SpringBootTest(
    webEnvironment = WebEnvironment.RANDOM_PORT,
    properties =
        [
            "sporesync.frontend.required=true",
            "sporesync.frontend.dist=backend/src/test/resources/test-frontend",
        ],
)
class FrontendWebMvcConfigurerTest(
    @LocalServerPort private val port: Int,
) {
    @Test
    fun `root serves the frontend bundle`() {
        val response = get("/")

        assertEquals(HTML_OK, response.statusCode())
        assertTrue(response.headers().firstValue("content-type").orElse("").contains("text/html"))
        assertTrue(response.body().contains("<div id=\"root\"></div>"))
    }

    @Test
    fun `nested client route falls back to index html`() {
        val response = get("/a/nested/route")

        assertEquals(HTML_OK, response.statusCode())
        assertTrue(response.headers().firstValue("content-type").orElse("").contains("text/html"))
        assertTrue(response.body().contains("<div id=\"root\"></div>"))
    }

    @Test
    fun `missing asset with extension returns 404`() {
        val response = get("/assets/missing.js")
        assertEquals(NOT_FOUND, response.statusCode())
    }

    @Test
    fun `actuator health route stays on backend routing`() {
        val response = get("/actuator/health")
        val status = JsonMapper.shared().readTree(response.body()).get(STATUS_FIELD).asString()

        assertEquals(HTML_OK, response.statusCode())
        assertEquals(STATUS_UP, status)
    }

    @Test
    fun `api routes stay on backend routing`() {
        val response = get("/api/x", "application/json")
        val body = JsonMapper.shared().readTree(response.body())
        val errorStatus = body.get(STATUS_FIELD).asInt()
        val errorPath = body.get(PATH_FIELD).asString()

        assertEquals(NOT_FOUND, response.statusCode())
        assertEquals(NOT_FOUND, errorStatus)
        assertEquals("/api/x", errorPath)
    }

    private fun get(path: String, accept: String? = null): HttpResponse<String> {
        val requestBuilder = HttpRequest.newBuilder(URI.create("http://localhost:$port$path"))
        if (accept != null) requestBuilder.header("Accept", accept)
        val request = requestBuilder.build()
        return HttpClient.newHttpClient().send(request, HttpResponse.BodyHandlers.ofString())
    }
}
