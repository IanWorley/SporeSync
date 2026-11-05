package com.sporesync.localscan.internal

import com.sporesync.filestate.STATE_DIRECTORY_NAME
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
import java.nio.file.attribute.BasicFileAttributes
import java.nio.file.attribute.FileTime
import java.nio.file.attribute.PosixFilePermissions
import java.time.Instant

private const val POSIX_VIEW = "posix"
private const val NO_PERMISSIONS = "---------"
private const val OWNER_PERMISSIONS = "rwx------"
private const val TOP_CONTENT = "12345"
private const val E01_CONTENT = "abc"
private const val E02_CONTENT = "de"
private val FILE_MODIFIED = Instant.parse("2026-01-02T03:04:05Z")
private val DIRECTORY_MODIFIED = Instant.parse("2026-02-03T04:05:06Z")

private fun sizeOf(content: String): Long = content.toByteArray().size.toLong()

class LocalScannerTest {
    @TempDir
    private lateinit var root: Path

    @Test
    fun `lists every entry with its relative path, size, and modification time`() {
        write("top.bin", TOP_CONTENT)
        write("show/s01/e01.mkv", E01_CONTENT)
        write("show/s01/e02.mkv", E02_CONTENT)
        touch("show/s01", DIRECTORY_MODIFIED)
        touch("show", DIRECTORY_MODIFIED)
        val seasonSize = sizeOf(E01_CONTENT) + sizeOf(E02_CONTENT)

        assertEquals(
            setOf(
                LocalFile("top.bin", sizeOf(TOP_CONTENT), FILE_MODIFIED, isDirectory = false),
                LocalFile("show", seasonSize, DIRECTORY_MODIFIED, isDirectory = true),
                LocalFile("show/s01", seasonSize, DIRECTORY_MODIFIED, isDirectory = true),
                LocalFile("show/s01/e01.mkv", sizeOf(E01_CONTENT), FILE_MODIFIED, isDirectory = false),
                LocalFile("show/s01/e02.mkv", sizeOf(E02_CONTENT), FILE_MODIFIED, isDirectory = false),
            ),
            LocalScanner.scan(root).toSet(),
        )
    }

    @Test
    fun `leaves out only the filestate directory at the root`() {
        write("$STATE_DIRECTORY_NAME/downloaded/show", "marker")
        write("show/$STATE_DIRECTORY_NAME", "user file")
        write("show/e01.mkv", "abc")

        assertEquals(
            setOf("show", "show/$STATE_DIRECTORY_NAME", "show/e01.mkv"),
            LocalScanner.scan(root).map { it.path }.toSet(),
        )
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
        assumeTrue(!Files.isReadable(show), "root reads a directory whatever its permissions")
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
    fun `drops a directory that vanishes mid-walk along with the entries already read from it`() {
        write("keep.mkv", E01_CONTENT)
        write("show/e01.mkv", E02_CONTENT)
        val show = root.resolve("show")
        val visitor = ScanVisitor(root)

        visitor.preVisitDirectory(root, attributes(root))
        visitor.visitFile(root.resolve("keep.mkv"), attributes(root.resolve("keep.mkv")))
        visitor.preVisitDirectory(show, attributes(show))
        visitor.visitFile(show.resolve("e01.mkv"), attributes(show.resolve("e01.mkv")))
        visitor.postVisitDirectory(show, NoSuchFileException("$show"))
        visitor.postVisitDirectory(root, null)

        assertEquals(
            listOf(LocalFile("keep.mkv", sizeOf(E01_CONTENT), FILE_MODIFIED, isDirectory = false)),
            visitor.files,
        )
    }

    @Test
    fun `follows a symbolic link at the root`() {
        write("downloads/e01.mkv", "abc")
        val link = Files.createSymbolicLink(root.resolve("link"), root.resolve("downloads"))

        assertEquals(listOf("e01.mkv"), LocalScanner.scan(link).map { it.path })
    }

    @Test
    fun `scans a root that is itself named like the filestate directory`() {
        write("$STATE_DIRECTORY_NAME/e01.mkv", "abc")

        assertEquals(listOf("e01.mkv"), LocalScanner.scan(root.resolve(STATE_DIRECTORY_NAME)).map { it.path })
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

    private fun attributes(path: Path): BasicFileAttributes =
        Files.readAttributes(path, BasicFileAttributes::class.java)

    private fun touch(
        relative: String,
        modified: Instant,
    ) {
        Files.setLastModifiedTime(root.resolve(relative), FileTime.from(modified))
    }
}
