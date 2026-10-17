package com.sporesync.localscan.internal

import com.sporesync.localscan.LocalFile
import java.io.IOException
import java.nio.file.FileVisitResult
import java.nio.file.Files
import java.nio.file.NoSuchFileException
import java.nio.file.NotDirectoryException
import java.nio.file.Path
import java.nio.file.SimpleFileVisitor
import java.nio.file.attribute.BasicFileAttributes
import java.time.Instant

/** Name of the `filestate` marker directory (and any stray marker file), which is never reported as content. */
private const val MARKER_NAME = ".sporesync"
private const val PATH_SEPARATOR = "/"

/**
 * Walks a local directory tree. A symbolic link at the root is followed, so the download directory may be a link;
 * links inside the tree are reported as entries but never followed.
 */
internal object LocalScanner {
    /**
     * Lists every entry under [root], excluding `.sporesync` markers. Throws [IOException] when [root] is missing or
     * any entry cannot be read, so an unmounted volume or unreadable subtree is never mistaken for deleted files.
     */
    fun scan(root: Path): List<LocalFile> {
        val start = root.toRealPath()
        if (!Files.isDirectory(start)) throw NotDirectoryException(root.toString())
        val visitor = ScanVisitor(start)
        Files.walkFileTree(start, visitor)
        return visitor.files
    }
}

/** Collects [LocalFile]s under [root]. Internal only so its failure handling can be tested without racing a delete. */
internal class ScanVisitor(
    private val root: Path,
) : SimpleFileVisitor<Path>() {
    /** A directory being walked; [size] grows as its children are visited. */
    private class OpenDirectory(
        val modified: Instant,
    ) {
        var size = 0L
    }

    val files = mutableListOf<LocalFile>()
    private val openDirectories = ArrayDeque<OpenDirectory>()

    override fun preVisitDirectory(
        dir: Path,
        attrs: BasicFileAttributes,
    ): FileVisitResult {
        // The root itself is the configured download directory, never a marker, whatever it is named.
        if (dir != root && isMarker(dir)) return FileVisitResult.SKIP_SUBTREE
        openDirectories.addLast(OpenDirectory(attrs.lastModifiedTime().toInstant()))
        return FileVisitResult.CONTINUE
    }

    override fun visitFile(
        file: Path,
        attrs: BasicFileAttributes,
    ): FileVisitResult {
        if (!isMarker(file)) add(file, attrs.size(), attrs.lastModifiedTime().toInstant(), isDirectory = false)
        return FileVisitResult.CONTINUE
    }

    override fun visitFileFailed(
        file: Path,
        exc: IOException,
    ): FileVisitResult {
        if (isVanishedDescendant(file, exc)) return FileVisitResult.CONTINUE
        throw exc
    }

    override fun postVisitDirectory(
        dir: Path,
        exc: IOException?,
    ): FileVisitResult {
        if (exc != null && !isVanishedDescendant(dir, exc)) throw exc
        val finished = openDirectories.removeLast()
        if (dir != root) add(dir, finished.size, finished.modified, isDirectory = true)
        return FileVisitResult.CONTINUE
    }

    private fun add(
        path: Path,
        size: Long,
        modified: Instant,
        isDirectory: Boolean,
    ) {
        files += LocalFile(root.relativize(path).joinToString(PATH_SEPARATOR), size, modified, isDirectory)
        openDirectories.last().size += size
    }

    // Transfers and deletes run alongside the scan, so an entry vanishing mid-walk is expected. Any other failure, or
    // the root itself vanishing, aborts the scan: a snapshot with entries missing would look like local deletes.
    private fun isVanishedDescendant(
        path: Path,
        exc: IOException,
    ): Boolean = path != root && exc is NoSuchFileException

    private fun isMarker(path: Path): Boolean = path.fileName?.toString() == MARKER_NAME
}
