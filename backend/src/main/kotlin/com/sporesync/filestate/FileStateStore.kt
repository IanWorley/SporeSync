package com.sporesync.filestate

import java.nio.file.NoSuchFileException
import java.nio.file.Path
import java.security.MessageDigest
import java.util.HexFormat
import kotlin.io.path.createDirectories
import kotlin.io.path.deleteIfExists
import kotlin.io.path.isDirectory
import kotlin.io.path.listDirectoryEntries
import kotlin.io.path.readBytes
import kotlin.io.path.writeText

/** Directory inside the download root that holds every marker. */
const val STATE_DIRECTORY_NAME = ".sporesync"

private const val MARKER_NAME_ALGORITHM = "SHA-256"
private const val PATH_SEPARATOR = "/"
private const val PARENT_SEGMENT = ".."

/**
 * Records which local files are downloaded or extracted as marker files under `<downloadRoot>/.sporesync`.
 *
 * Markers live on disk rather than in H2 so they survive a database reset.
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
        val file = markerFile(marker, path)
        if (file.isCompleteMarkerFor(path)) return
        markerDirectory(marker).createDirectories()
        file.writeText(path)
    }

    /** Returns every marker recorded for the file at [relativePath]. */
    fun markersOf(relativePath: String): Set<FileMarker> {
        val path = normalize(relativePath)
        return FileMarker.entries.filterTo(mutableSetOf()) { markerFile(it, path).isCompleteMarkerFor(path) }
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
                val markedPath = markerFile.markedPathOrNull() ?: return@filter false
                markedPath == path || markedPath.startsWith(descendantPrefix)
            }.forEach { it.deleteIfExists() }
    }

    private fun markerDirectory(marker: FileMarker): Path = stateDirectory.resolve(marker.directoryName)

    private fun markerFile(
        marker: FileMarker,
        normalizedPath: String,
    ): Path = markerDirectory(marker).resolve(markerName(normalizedPath))

    private fun normalize(relativePath: String): String {
        val candidate = Path.of(relativePath)
        require(candidate.root == null) { "Path must be relative to the download root: $relativePath" }

        val normalized = candidate.normalize()
        val segments = normalized.map(Path::toString)
        require(segments.isNotEmpty() && segments.first().isNotEmpty()) { "Path must name a file: $relativePath" }
        require(segments.first() != PARENT_SEGMENT) { "Path must not start with $PARENT_SEGMENT: $relativePath" }
        require(segments.first() != STATE_DIRECTORY_NAME) { "Path points into $STATE_DIRECTORY_NAME: $relativePath" }

        return segments.joinToString(PATH_SEPARATOR)
    }

    private fun Path.isCompleteMarkerFor(normalizedPath: String): Boolean =
        markerBytesOrNull()?.contentEquals(normalizedPath.toByteArray()) == true

    private fun Path.markedPathOrNull(): String? = markerBytesOrNull()?.decodeToString()

    private fun Path.markerBytesOrNull(): ByteArray? =
        try {
            readBytes()
        } catch (_: NoSuchFileException) {
            null
        }

    private fun markerName(normalizedPath: String): String {
        val digest = MessageDigest.getInstance(MARKER_NAME_ALGORITHM).digest(normalizedPath.toByteArray())
        return HexFormat.of().formatHex(digest)
    }
}
