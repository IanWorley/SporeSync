package com.sporesync.filestate

import org.junit.jupiter.api.Assertions.assertEquals
import org.junit.jupiter.api.Assertions.assertTrue
import org.junit.jupiter.api.Test
import org.junit.jupiter.api.assertThrows
import org.junit.jupiter.api.io.TempDir
import org.junit.jupiter.params.ParameterizedTest
import org.junit.jupiter.params.provider.ValueSource
import java.nio.file.Path
import kotlin.io.path.readBytes
import kotlin.io.path.walk
import kotlin.io.path.writeBytes

private const val MOVIE = "movies/film.mkv"
private const val SHOW_DIRECTORY = "shows/series"
private const val EPISODE = "shows/series/episode.mkv"
private const val SIBLING_SHOW_DIRECTORY = "shows/series-two"
private const val NON_ASCII_MOVIE = "movies/amélie.mkv"

class FileStateStoreTest {
    @TempDir
    private lateinit var downloadRoot: Path

    @Test
    fun `markers survive a new store instance`() {
        FileStateStore(downloadRoot).run {
            mark(MOVIE, FileMarker.DOWNLOADED)
            mark(MOVIE, FileMarker.EXTRACTED)
        }

        val restarted = FileStateStore(downloadRoot)

        assertEquals(setOf(FileMarker.DOWNLOADED, FileMarker.EXTRACTED), restarted.markersOf(MOVIE))
    }

    @Test
    fun `unmarked file has no markers`() {
        FileStateStore(downloadRoot).mark(MOVIE, FileMarker.DOWNLOADED)

        assertEquals(emptySet<FileMarker>(), FileStateStore(downloadRoot).markersOf(EPISODE))
    }

    @Test
    fun `marking an already marked file keeps it marked`() {
        val store = FileStateStore(downloadRoot)
        store.mark(MOVIE, FileMarker.DOWNLOADED)

        store.mark(MOVIE, FileMarker.DOWNLOADED)

        assertEquals(setOf(FileMarker.DOWNLOADED), store.markersOf(MOVIE))
    }

    @Test
    fun `a marker cut short by a crash is not reported and is repaired by the next mark`() {
        val store = FileStateStore(downloadRoot)
        store.mark(NON_ASCII_MOVIE, FileMarker.DOWNLOADED)
        val markerFile = downloadRoot.resolve(STATE_DIRECTORY_NAME).walk().single()
        val throughFirstByteOfAccent = NON_ASCII_MOVIE.substringBefore('é').toByteArray().size + 1
        markerFile.writeBytes(markerFile.readBytes().copyOf(throughFirstByteOfAccent))

        assertEquals(emptySet<FileMarker>(), store.markersOf(NON_ASCII_MOVIE))

        store.mark(NON_ASCII_MOVIE, FileMarker.DOWNLOADED)

        assertEquals(setOf(FileMarker.DOWNLOADED), store.markersOf(NON_ASCII_MOVIE))
    }

    @Test
    fun `equivalent spellings of a path share markers`() {
        val store = FileStateStore(downloadRoot)

        store.mark("./movies/../movies/film.mkv", FileMarker.DOWNLOADED)

        assertEquals(setOf(FileMarker.DOWNLOADED), store.markersOf(MOVIE))
    }

    @Test
    fun `clear removes markers for the deleted path and everything beneath it`() {
        val store = FileStateStore(downloadRoot)
        store.mark(SHOW_DIRECTORY, FileMarker.DOWNLOADED)
        store.mark(EPISODE, FileMarker.EXTRACTED)
        store.mark(MOVIE, FileMarker.DOWNLOADED)
        store.mark(SIBLING_SHOW_DIRECTORY, FileMarker.DOWNLOADED)

        store.clear(SHOW_DIRECTORY)

        assertEquals(emptySet<FileMarker>(), store.markersOf(SHOW_DIRECTORY))
        assertEquals(emptySet<FileMarker>(), store.markersOf(EPISODE))
        assertEquals(setOf(FileMarker.DOWNLOADED), store.markersOf(MOVIE))
        assertEquals(setOf(FileMarker.DOWNLOADED), store.markersOf(SIBLING_SHOW_DIRECTORY))
    }

    @Test
    fun `markers are written only inside the state directory`() {
        FileStateStore(downloadRoot).mark(EPISODE, FileMarker.DOWNLOADED)

        val stateDirectory = downloadRoot.resolve(STATE_DIRECTORY_NAME)
        val markerFiles = downloadRoot.walk().toList()
        assertTrue(markerFiles.isNotEmpty())
        assertTrue(markerFiles.all { it.startsWith(stateDirectory) })
    }

    @ParameterizedTest
    @ValueSource(
        strings = [
            "",
            ".",
            "movies/..",
            "../outside.mkv",
            "movies/../../outside.mkv",
            "/etc/passwd",
            ".sporesync/downloaded",
        ],
    )
    fun `rejects paths that do not name a file inside the download root`(path: String) {
        assertThrows<IllegalArgumentException> { FileStateStore(downloadRoot).mark(path, FileMarker.DOWNLOADED) }
    }

    @Test
    fun `rejects parent paths that re-enter the download root`() {
        val alias = "../${downloadRoot.fileName}/$MOVIE"

        assertThrows<IllegalArgumentException> { FileStateStore(downloadRoot).mark(alias, FileMarker.DOWNLOADED) }
    }
}
