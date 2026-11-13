package com.sporesync.remotescan

import org.junit.jupiter.api.Assertions.assertEquals
import org.junit.jupiter.api.Assertions.assertNotEquals
import org.junit.jupiter.api.Assertions.assertTrue
import org.junit.jupiter.api.Test
import org.junit.jupiter.api.io.TempDir
import tools.jackson.databind.JsonNode
import tools.jackson.databind.json.JsonMapper
import java.nio.file.Files
import java.nio.file.Path
import java.nio.file.attribute.PosixFilePermission
import kotlin.io.path.createDirectories
import kotlin.io.path.createSymbolicLinkPointingTo
import kotlin.io.path.writeBytes
import kotlin.io.path.writeText

private const val FILM = "abc"
private const val EPISODE = "hello"
private const val PARTIAL_FILE_SIZE = 100
private const val PARTIAL_DOWNLOADED_SIZE = 20L
private const val LFTP_STATUS = "size=100\n0.pos=10\n0.limit=50\n1.pos=60\n1.limit=100\n"

private data class ScanResult(
    val exitCode: Int,
    val stdout: String,
    val stderr: String,
)

class ScannerScriptTest {
    @TempDir
    private lateinit var tempDir: Path

    private val root: Path by lazy { tempDir.resolve("remote").createDirectories() }

    @Test
    fun `scanner prints one json list in tree order with directory sizes summed from children`() {
        root.resolve("shows/season1").createDirectories()
        root.resolve("movies").createDirectories()
        root.resolve("movies/film.mkv").writeText(FILM)
        root.resolve("shows/season1/episode.mkv").writeText(EPISODE)

        val entries = scanEntries()

        assertEquals(
            listOf("movies", "movies/film.mkv", "shows", "shows/season1", "shows/season1/episode.mkv"),
            entries.map { it.get("path").asString() },
        )
        val byPath = entries.associateBy { it.get("path").asString() }
        assertEquals(FILM.length.toLong(), byPath.getValue("movies").get("size").asLong())
        assertEquals(EPISODE.length.toLong(), byPath.getValue("shows").get("size").asLong())
        assertTrue(byPath.getValue("shows").get("is_dir").asBoolean())
        assertEquals(setOf("path", "size", "mtime", "is_dir"), byPath.getValue("movies/film.mkv").propertyNames())
    }

    @Test
    fun `scanner follows symlinks and skips broken ones`() {
        root.resolve("movies").createDirectories()
        root.resolve("movies/film.mkv").writeText(FILM)
        root.resolve("linked-dir").createSymbolicLinkPointingTo(Path.of("movies"))
        root.resolve("linked-file").createSymbolicLinkPointingTo(Path.of("movies/film.mkv"))
        root.resolve("broken").createSymbolicLinkPointingTo(Path.of("missing"))

        val byPath = scanEntries().associateBy { it.get("path").asString() }

        assertEquals(
            setOf("linked-dir", "linked-dir/film.mkv", "linked-file", "movies", "movies/film.mkv"),
            byPath.keys,
        )
        assertTrue(byPath.getValue("linked-dir").get("is_dir").asBoolean())
        assertEquals(FILM.length.toLong(), byPath.getValue("linked-file").get("size").asLong())
    }

    @Test
    fun `scanner hides lftp status files and reports downloaded bytes of partial files`() {
        root.resolve("partial.bin").writeBytes(ByteArray(PARTIAL_FILE_SIZE))
        root.resolve("partial.bin.lftp-pget-status").writeText(LFTP_STATUS)

        val entries = scanEntries()

        assertEquals(listOf("partial.bin"), entries.map { it.get("path").asString() })
        assertEquals(PARTIAL_DOWNLOADED_SIZE, entries.single().get("size").asLong())
    }

    @Test
    fun `scanner reports a missing root as a SystemScannerError`() {
        val result = scan(tempDir.resolve("missing"))

        assertNotEquals(0, result.exitCode)
        assertTrue(result.stderr.contains("SystemScannerError: Path does not exist:"), result.stderr)
    }

    @Test
    fun `scanner reports a file root as a SystemScannerError`() {
        val file = tempDir.resolve("file.txt")
        file.writeText(FILM)

        val result = scan(file)

        assertNotEquals(0, result.exitCode)
        assertTrue(result.stderr.contains("SystemScannerError: Path is not a directory:"), result.stderr)
    }

    @Test
    fun `scanner fails the scan on an unreadable directory`() {
        val unreadable = root.resolve("private").createDirectories()
        try {
            Files.setPosixFilePermissions(unreadable, emptySet<PosixFilePermission>())

            val result = scan(root)

            assertNotEquals(0, result.exitCode)
            assertTrue(result.stderr.contains("PermissionError"), result.stderr)
        } finally {
            Files.setPosixFilePermissions(unreadable, PosixFilePermission.entries.toSet())
        }
    }

    private fun scanEntries(): List<JsonNode> {
        val result = scan(root)
        assertEquals(0, result.exitCode, result.stderr)
        return JsonMapper.shared().readTree(result.stdout).toList()
    }

    private fun scan(target: Path): ScanResult {
        val scriptPath = tempDir.resolve("scanner.py")
        scriptPath.writeBytes(EmbeddedScannerScript().bytes)
        val process = ProcessBuilder("python3", scriptPath.toString(), target.toString()).start()
        val stdout = process.inputStream.readAllBytes().decodeToString()
        val stderr = process.errorStream.readAllBytes().decodeToString()
        return ScanResult(process.waitFor(), stdout, stderr)
    }
}
