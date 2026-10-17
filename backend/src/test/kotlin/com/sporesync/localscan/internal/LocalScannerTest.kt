package com.sporesync.localscan.internal

import com.sporesync.localscan.LocalFile
import org.junit.jupiter.api.Assertions.assertEquals
import org.junit.jupiter.api.Test
import org.junit.jupiter.api.assertThrows
import org.junit.jupiter.api.io.TempDir
import java.nio.file.Files
import java.nio.file.NotDirectoryException
import java.nio.file.Path
import java.nio.file.attribute.FileTime
import java.time.Instant

private const val MARKER = ".sporesync"
private val FILE_MODIFIED = Instant.parse("2026-01-02T03:04:05Z")
private val DIRECTORY_MODIFIED = Instant.parse("2026-02-03T04:05:06Z")

class LocalScannerTest {
    @TempDir
    private lateinit var root: Path

    @Test
    fun `lists every entry with its relative path, size, and modification time`() {
        write("top.bin", "12345")
        write("show/s01/e01.mkv", "abc")
        write("show/s01/e02.mkv", "de")
        touch("show/s01", DIRECTORY_MODIFIED)
        touch("show", DIRECTORY_MODIFIED)

        assertEquals(
            setOf(
                LocalFile("top.bin", size = 5, FILE_MODIFIED, isDirectory = false),
                LocalFile("show", size = 5, DIRECTORY_MODIFIED, isDirectory = true),
                LocalFile("show/s01", size = 5, DIRECTORY_MODIFIED, isDirectory = true),
                LocalFile("show/s01/e01.mkv", size = 3, FILE_MODIFIED, isDirectory = false),
                LocalFile("show/s01/e02.mkv", size = 2, FILE_MODIFIED, isDirectory = false),
            ),
            LocalScanner.scan(root).toSet(),
        )
    }

    @Test
    fun `leaves out sporesync marker directories and files`() {
        write("$MARKER/downloaded/show", "marker")
        write("show/$MARKER", "marker")
        write("show/e01.mkv", "abc")

        val scanned = LocalScanner.scan(root).associateBy { it.path }

        assertEquals(setOf("show", "show/e01.mkv"), scanned.keys)
        assertEquals(3L, scanned.getValue("show").size)
    }

    @Test
    fun `fails rather than reporting an empty tree when the directory is missing`() {
        assertThrows<NotDirectoryException> { LocalScanner.scan(root.resolve("unmounted")) }
    }

    private fun write(
        relative: String,
        content: String,
    ) {
        val file = root.resolve(relative)
        Files.createDirectories(file.parent)
        Files.writeString(file, content)
        touch(relative, FILE_MODIFIED)
    }

    private fun touch(
        relative: String,
        modified: Instant,
    ) {
        Files.setLastModifiedTime(root.resolve(relative), FileTime.from(modified))
    }
}
