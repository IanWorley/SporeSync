package com.sporesync.localscan.internal

import com.sporesync.localscan.LocalFile
import org.junit.jupiter.api.Assertions.assertEquals
import org.junit.jupiter.api.Assumptions.assumeTrue
import org.junit.jupiter.api.Test
import org.junit.jupiter.api.assertThrows
import org.junit.jupiter.api.io.TempDir
import java.io.IOException
import java.nio.file.FileVisitResult
import java.nio.file.Files
import java.nio.file.NoSuchFileException
import java.nio.file.Path
import java.nio.file.attribute.FileTime
import java.nio.file.attribute.PosixFilePermissions
import java.time.Instant

private const val MARKER = ".sporesync"
private const val POSIX_VIEW = "posix"
private const val NO_PERMISSIONS = "---------"
private const val OWNER_PERMISSIONS = "rwx------"
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
        assertThrows<IOException> { LocalScanner.scan(root.resolve("unmounted")) }
    }

    @Test
    fun `fails rather than reporting a partial tree when a subdirectory is unreadable`() {
        assumeTrue(root.fileSystem.supportedFileAttributeViews().contains(POSIX_VIEW))
        write("show/e01.mkv", "abc")
        val show = root.resolve("show")
        Files.setPosixFilePermissions(show, PosixFilePermissions.fromString(NO_PERMISSIONS))
        // Running as root ignores permissions, so there is nothing to prove there.
        assumeTrue(!Files.isReadable(show))
        try {
            assertThrows<IOException> { LocalScanner.scan(root) }
        } finally {
            Files.setPosixFilePermissions(show, PosixFilePermissions.fromString(OWNER_PERMISSIONS))
        }
    }

    @Test
    fun `aborts when the root vanishes mid-walk but skips a vanished descendant`() {
        val visitor = ScanVisitor(root)
        val descendant = root.resolve("e01.mkv")

        assertEquals(FileVisitResult.CONTINUE, visitor.visitFileFailed(descendant, NoSuchFileException("$descendant")))
        assertThrows<NoSuchFileException> { visitor.visitFileFailed(root, NoSuchFileException("$root")) }
    }

    @Test
    fun `follows a symbolic link at the root`() {
        write("downloads/e01.mkv", "abc")
        val link = Files.createSymbolicLink(root.resolve("link"), root.resolve("downloads"))

        assertEquals(listOf("e01.mkv"), LocalScanner.scan(link).map { it.path })
    }

    @Test
    fun `scans a root that is itself named like the marker`() {
        write("$MARKER/e01.mkv", "abc")

        assertEquals(listOf("e01.mkv"), LocalScanner.scan(root.resolve(MARKER)).map { it.path })
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
