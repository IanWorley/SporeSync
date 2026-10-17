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

private const val FILESTATE_MARKER_NAME = ".sporesync"
private const val PATH_SEPARATOR = "/"

internal object LocalScanner {
    fun scan(root: Path): List<LocalFile> {
        val realRoot = root.toRealPath()
        if (!Files.isDirectory(realRoot)) throw NotDirectoryException(root.toString())
        val visitor = ScanVisitor(realRoot)
        Files.walkFileTree(realRoot, visitor)
        return visitor.files
    }
}

internal class ScanVisitor(
    private val root: Path,
) : SimpleFileVisitor<Path>() {
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
        if (isMarker(dir)) return FileVisitResult.SKIP_SUBTREE
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

    private fun isVanishedDescendant(
        path: Path,
        exc: IOException,
    ): Boolean = path != root && exc is NoSuchFileException

    private fun isMarker(path: Path): Boolean = path != root && path.fileName?.toString() == FILESTATE_MARKER_NAME
}
