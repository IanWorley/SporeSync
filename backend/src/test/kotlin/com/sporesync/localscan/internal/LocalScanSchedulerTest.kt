package com.sporesync.localscan.internal

import com.sporesync.config.ConfigChanged
import com.sporesync.config.SettingsStore
import com.sporesync.localscan.LocalScanned
import org.junit.jupiter.api.AfterEach
import org.junit.jupiter.api.Assertions.assertEquals
import org.junit.jupiter.api.Test
import org.junit.jupiter.api.io.TempDir
import org.springframework.context.ApplicationEventPublisher
import org.springframework.mock.env.MockEnvironment
import java.nio.file.Files
import java.nio.file.Path
import java.time.Duration
import java.util.concurrent.LinkedBlockingQueue
import java.util.concurrent.TimeUnit

private const val SETTINGS_FILE_NAME = "settings.yaml"
private const val DOWNLOADS_DIR_NAME = "downloads"
private const val EVENT_TIMEOUT_SECONDS = 5L

private val NEVER_AGAIN = Duration.ofHours(1)
private val SHORT_INTERVAL = Duration.ofMillis(10)
private val BEYOND_NANOSECONDS = Duration.ofNanos(Long.MAX_VALUE).plusDays(1)

class LocalScanSchedulerTest {
    @TempDir
    private lateinit var directory: Path

    private val published = LinkedBlockingQueue<LocalScanned>()
    private val publisher = ApplicationEventPublisher { event -> if (event is LocalScanned) published.add(event) }
    private lateinit var scheduler: LocalScanScheduler

    @AfterEach
    fun stopScheduler() {
        scheduler.stop()
    }

    @Test
    fun `publishes a scan of the configured local directory`() {
        val downloads = Files.createDirectories(directory.resolve(DOWNLOADS_DIR_NAME))
        Files.writeString(downloads.resolve("e01.mkv"), "abc")
        scheduler = LocalScanScheduler(storeScanning(downloads, NEVER_AGAIN), publisher)

        scheduler.start()

        val scan = nextScan()
        assertEquals(downloads, scan.root)
        assertEquals(listOf("e01.mkv"), scan.files.map { it.path })
    }

    @Test
    fun `a changed local interval takes effect without a restart`() {
        val downloads = Files.createDirectories(directory.resolve(DOWNLOADS_DIR_NAME))
        val store = storeScanning(downloads, NEVER_AGAIN)
        scheduler = LocalScanScheduler(store, publisher)
        scheduler.start()
        nextScan()

        val previous = store.current
        val current = store.update { it.copy(scan = it.scan.copy(localInterval = SHORT_INTERVAL)) }
        scheduler.on(ConfigChanged(previous, current))

        nextScan()
        nextScan()
    }

    @Test
    fun `an interval too long for nanoseconds still reschedules the scan`() {
        val downloads = Files.createDirectories(directory.resolve(DOWNLOADS_DIR_NAME))
        val store = storeScanning(downloads, NEVER_AGAIN)
        scheduler = LocalScanScheduler(store, publisher)
        scheduler.start()
        nextScan()

        val previous = store.current
        val current = store.update { it.copy(scan = it.scan.copy(localInterval = BEYOND_NANOSECONDS)) }
        scheduler.on(ConfigChanged(previous, current))

        nextScan()
    }

    private fun storeScanning(
        downloads: Path,
        interval: Duration,
    ): SettingsStore =
        SettingsStore(MockEnvironment(), { }, directory.resolve(SETTINGS_FILE_NAME)).apply {
            update {
                it.copy(
                    paths = it.paths.copy(localDir = downloads),
                    scan = it.scan.copy(localInterval = interval),
                )
            }
        }

    private fun nextScan(): LocalScanned =
        checkNotNull(published.poll(EVENT_TIMEOUT_SECONDS, TimeUnit.SECONDS)) { "no LocalScanned was published" }
}
