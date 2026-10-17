package com.sporesync.filestate

import java.nio.file.Path
import java.nio.file.StandardCopyOption.ATOMIC_MOVE
import java.nio.file.StandardCopyOption.REPLACE_EXISTING
import java.security.MessageDigest
import java.util.HexFormat
import kotlin.io.path.createDirectories
import kotlin.io.path.createTempFile
import kotlin.io.path.deleteIfExists
import kotlin.io.path.exists
import kotlin.io.path.isDirectory
import kotlin.io.path.listDirectoryEntries
import kotlin.io.path.moveTo
import kotlin.io.path.readText
import kotlin.io.path.writeText

/** Directory inside the download root that holds every marker. */
const val STATE_DIRECTORY_NAME = ".sporesync"

private const val MARKER_NAME_ALGORITHM = "SHA-256"
private const val PATH_SEPARATOR = "/"
private const val PARENT_SEGMENT = ".."
private const val PENDING_MARKER_PREFIX = "pending-"

/**
 * Records which local files are downloaded or extracted as marker files under `<downloadRoot>/.sporesync`.
 *
 * Markers live on disk rather than in H2 so they survive a database reset. Each marker is named by a hash of the
 * file's normalized relative path, so no input can place a marker outside its state directory, and stores that
 * path as its content so a directory delete can clear markers for everything inside it.
 */
class FileStateStore(
    downloadRoot: Path,
) {
    private val root: Path = downloadRoot.toAbsolutePath().normalize()
    private val stateDirectory: Path = root.resolve(STATE_DIRECTORY_NAME)

    /** Records [marker] for the file at [relativePath]. Marking an already-marked file is a no-op. */
    fun mark(
        relativePath: String,
        marker: FileMarker,
    ) {
        val path = normalize(relativePath)
        markerDirectory(marker).createDirectories()
        // Write beside the marker directories, then move into place, so readers never see a partial marker.
        val pending = createTempFile(stateDirectory, PENDING_MARKER_PREFIX)
        try {
            pending.writeText(path)
            pending.moveTo(markerFile(marker, path), ATOMIC_MOVE, REPLACE_EXISTING)
        } finally {
            pending.deleteIfExists()
        }
    }

    /** Returns every marker recorded for the file at [relativePath]. */
    fun markersOf(relativePath: String): Set<FileMarker> {
        val path = normalize(relativePath)
        return FileMarker.entries.filterTo(mutableSetOf()) { markerFile(it, path).exists() }
    }

    /**
     * Removes all markers for [relativePath] and anything beneath it. Call this once the file or directory has been
     * deleted locally so a later download of the same path starts clean.
     */
    fun clear(relativePath: String) {
        val path = normalize(relativePath)
        val descendantPrefix = path + PATH_SEPARATOR
        FileMarker.entries
            .map(::markerDirectory)
            .filter { it.isDirectory() }
            .flatMap { it.listDirectoryEntries() }
            .filter { markerFile ->
                val markedPath = markerFile.readText()
                markedPath == path || markedPath.startsWith(descendantPrefix)
            }.forEach { it.deleteIfExists() }
    }

    private fun markerDirectory(marker: FileMarker): Path = stateDirectory.resolve(marker.directoryName)

    private fun markerFile(
        marker: FileMarker,
        normalizedPath: String,
    ): Path = markerDirectory(marker).resolve(markerName(normalizedPath))

    /**
     * Normalizes [relativePath] to `/`-separated segments with `.` removed and rejects anything that is absolute,
     * empty, escapes the download root, or points into the `.sporesync` directory itself.
     */
    private fun normalize(relativePath: String): String {
        val candidate = Path.of(relativePath)
        require(!candidate.isAbsolute) { "Path must be relative to the download root: $relativePath" }

        val normalized = candidate.normalize()
        val segments = normalized.map(Path::toString)
        require(segments.isNotEmpty() && segments.first().isNotEmpty()) { "Path must name a file: $relativePath" }
        // After normalize(), `..` can only lead the path. Rejecting it outright also rejects aliases such as
        // `../downloads/a` that re-enter the root, which would otherwise get a second marker for the same file.
        require(segments.first() != PARENT_SEGMENT) { "Path escapes the download root: $relativePath" }
        require(segments.first() != STATE_DIRECTORY_NAME) { "Path points into $STATE_DIRECTORY_NAME: $relativePath" }

        return segments.joinToString(PATH_SEPARATOR)
    }

    private fun markerName(normalizedPath: String): String {
        val digest = MessageDigest.getInstance(MARKER_NAME_ALGORITHM).digest(normalizedPath.toByteArray())
        return HexFormat.of().formatHex(digest)
    }
}
