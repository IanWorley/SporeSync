package com.sporesync.remotescan

import org.junit.jupiter.api.Assertions.assertEquals
import org.junit.jupiter.api.Assertions.assertFalse
import org.junit.jupiter.api.Assertions.assertTrue
import org.junit.jupiter.api.Test
import org.junit.jupiter.api.io.TempDir
import tools.jackson.databind.json.JsonMapper
import java.nio.file.Path
import kotlin.io.path.createDirectories
import kotlin.io.path.writeBytes
import kotlin.io.path.writeText

class ScannerScriptTest {
    @TempDir
    private lateinit var tempDir: Path

    @Test
    fun `scanner prints json entries with relative path size mtime and directory flag`() {
        val root = tempDir.resolve("remote")
        val nestedDirectory = root.resolve("shows/season1")
        nestedDirectory.createDirectories()
        root.resolve("movies").createDirectories()
        root.resolve("movies/film.mkv").writeText("abc")
        nestedDirectory.resolve("episode.mkv").writeText("hello")

        val scriptPath = tempDir.resolve("scanner.py")
        scriptPath.writeBytes(EmbeddedScannerScript().bytes)

        val process = ProcessBuilder("python3", scriptPath.toString(), root.toString()).start()
        val stdout = process.inputStream.readAllBytes().decodeToString()
        val stderr = process.errorStream.readAllBytes().decodeToString()
        val exitCode = process.waitFor()

        assertEquals(0, exitCode, stderr)
        val entries = JsonMapper.shared().readTree(stdout)
        assertTrue(entries.isArray)
        val byPath = entries.associateBy { it.get("path").asText() }

        assertEquals(
            setOf("movies", "movies/film.mkv", "shows", "shows/season1", "shows/season1/episode.mkv"),
            byPath.keys,
        )
        assertTrue(byPath.getValue("movies").get("is_dir").asBoolean())
        assertEquals(3, byPath.getValue("movies/film.mkv").get("size").asLong())
        assertFalse(byPath.getValue("movies/film.mkv").get("is_dir").asBoolean())
        assertTrue(byPath.getValue("shows/season1").get("is_dir").asBoolean())
        assertEquals(5, byPath.getValue("shows/season1/episode.mkv").get("size").asLong())
        assertTrue(byPath.values.all { it.has("mtime") && it.get("mtime").isNumber })
    }
}
