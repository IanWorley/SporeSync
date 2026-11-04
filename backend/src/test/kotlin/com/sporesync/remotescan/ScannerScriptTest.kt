package com.sporesync.remotescan

import org.junit.jupiter.api.Assertions.assertEquals
import org.junit.jupiter.api.Assertions.assertFalse
import org.junit.jupiter.api.Assertions.assertTrue
import org.junit.jupiter.api.Test
import org.junit.jupiter.api.io.TempDir
import tools.jackson.databind.json.JsonMapper
import java.nio.file.Files
import java.nio.file.Path
import java.nio.file.attribute.PosixFilePermission
import kotlin.io.path.createDirectories
import kotlin.io.path.writeBytes
import kotlin.io.path.writeText

class ScannerScriptTest {
    @TempDir
    private lateinit var tempDir: Path

    @Test
    fun `scanner prints json lines with relative path size mtime dir flag and type`() {
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
        val entries =
            stdout
                .lineSequence()
                .filter(String::isNotBlank)
                .map(JsonMapper.shared()::readTree)
                .toList()
        val byPath = entries.associateBy { it.get("path").asText() }

        assertEquals(
            setOf("movies", "movies/film.mkv", "shows", "shows/season1", "shows/season1/episode.mkv"),
            byPath.keys,
        )
        assertTrue(byPath.getValue("movies").get("is_dir").asBoolean())
        assertEquals("dir", byPath.getValue("movies").get("type").asText())
        assertEquals(3, byPath.getValue("movies/film.mkv").get("size").asLong())
        assertFalse(byPath.getValue("movies/film.mkv").get("is_dir").asBoolean())
        assertEquals("file", byPath.getValue("movies/film.mkv").get("type").asText())
        assertTrue(byPath.getValue("shows/season1").get("is_dir").asBoolean())
        assertEquals(5, byPath.getValue("shows/season1/episode.mkv").get("size").asLong())
        assertEquals("file", byPath.getValue("shows/season1/episode.mkv").get("type").asText())
        assertTrue(entries.all { it.has("mtime") && it.get("mtime").isNumber })
    }

    @Test
    fun `scanner skips unreadable directories without failing`() {
        val root = tempDir.resolve("remote")
        val unreadable = root.resolve("private")
        unreadable.createDirectories()
        root.resolve("public").createDirectories()
        root.resolve("public/visible.txt").writeText("ok")
        unreadable.resolve("hidden.txt").writeText("secret")

        val scriptPath = tempDir.resolve("scanner.py")
        scriptPath.writeBytes(EmbeddedScannerScript().bytes)

        try {
            Files.setPosixFilePermissions(unreadable, emptySet<PosixFilePermission>())
            val process = ProcessBuilder("python3", scriptPath.toString(), root.toString()).start()
            val stdout = process.inputStream.readAllBytes().decodeToString()
            val stderr = process.errorStream.readAllBytes().decodeToString()
            val exitCode = process.waitFor()

            assertEquals(0, exitCode, stderr)
            val entries =
                stdout
                    .lineSequence()
                    .filter(String::isNotBlank)
                    .map(JsonMapper.shared()::readTree)
                    .toList()
            val paths = entries.map { it.get("path").asText() }.toSet()
            assertTrue(paths.contains("private"))
            assertTrue(paths.contains("public/visible.txt"))
            assertFalse(paths.contains("private/hidden.txt"))
            assertTrue(stderr.contains("skip private: scandir"))
        } finally {
            Files.setPosixFilePermissions(
                unreadable,
                setOf(
                    PosixFilePermission.OWNER_READ,
                    PosixFilePermission.OWNER_WRITE,
                    PosixFilePermission.OWNER_EXECUTE,
                ),
            )
        }
    }
}
