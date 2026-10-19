package com.sporesync.transfer

import com.sporesync.config.SettingsStore
import com.sporesync.transfer.internal.Downloader
import org.junit.jupiter.api.AfterEach
import org.junit.jupiter.api.Assertions.assertEquals
import org.junit.jupiter.api.Assertions.assertNull
import org.junit.jupiter.api.Assertions.assertTrue
import org.junit.jupiter.api.Test
import org.junit.jupiter.api.assertThrows
import org.junit.jupiter.api.io.TempDir
import org.springframework.context.ApplicationEventPublisher
import org.springframework.mock.env.MockEnvironment
import java.io.IOException
import java.nio.channels.ClosedByInterruptException
import java.nio.file.Path
import java.time.Duration
import java.util.concurrent.CountDownLatch
import java.util.concurrent.LinkedBlockingQueue
import java.util.concurrent.TimeUnit

private const val SETTINGS_FILE_NAME = "settings.yaml"
private const val CONCURRENT_LIMIT = 2
private const val FAILURE_MESSAGE = "remote file vanished"
private const val LISTENER_FAILURE = "listener broke"
private val TIMEOUT: Duration = Duration.ofSeconds(5)

// Long enough that a transfer which should stay queued would have started.
private val QUIET_PERIOD: Duration = Duration.ofMillis(200)

/** Queue semantics against a fake downloader, so no SSH server is needed. */
class TransferQueueTest {
    @TempDir
    private lateinit var directory: Path

    private val events = RecordingEvents()
    private val started = LinkedBlockingQueue<String>()
    private val release = CountDownLatch(1)
    private val interrupted = CountDownLatch(1)
    private var queue: TransferQueue? = null

    /** Blocks every download until [release], recording starts and interrupts. */
    private val blockingDownloader =
        Downloader { path, _ ->
            started.put(path)
            try {
                release.await()
            } catch (e: InterruptedException) {
                interrupted.countDown()
                throw e
            }
        }

    @AfterEach
    fun closeQueue() {
        release.countDown()
        queue?.close()
    }

    @Test
    fun `runs at most the concurrent file limit at once`() {
        val queue = queue(blockingDownloader, CONCURRENT_LIMIT)

        listOf("a", "b", "c").forEach(queue::enqueue)

        assertEquals(setOf("a", "b"), setOf(nextStart(), nextStart()))
        assertNull(started.poll(QUIET_PERIOD.toMillis(), TimeUnit.MILLISECONDS))
        release.countDown()
        assertEquals("c", nextStart())
    }

    @Test
    fun `enqueuing a path that is already queued does nothing`() {
        val queue = queue(blockingDownloader, CONCURRENT_LIMIT)

        queue.enqueue("a")
        queue.enqueue("a/")
        release.countDown()
        events.await<TransferCompleted>(TIMEOUT)
        queue.close()

        assertEquals(listOf("a"), started.toList())
    }

    @Test
    fun `enqueuing overlapping paths does nothing`() {
        val queue = queue(blockingDownloader, CONCURRENT_LIMIT)
        queue.enqueue("a")
        assertEquals("a", nextStart())

        queue.enqueue("a/child")
        release.countDown()
        events.await<TransferCompleted>(TIMEOUT) { it.path == "a" }

        assertNull(started.poll(QUIET_PERIOD.toMillis(), TimeUnit.MILLISECONDS))
    }

    @Test
    fun `stopping a queued transfer publishes TransferStopped and never runs it`() {
        val queue = queue(blockingDownloader, 1)
        queue.enqueue("a")
        nextStart()

        queue.enqueue("b")
        queue.stop("b")
        release.countDown()
        // One worker runs the queue in order, so c finishing means b was skipped rather than not yet reached.
        queue.enqueue("c")
        events.await<TransferCompleted>(TIMEOUT) { it.path == "c" }

        assertEquals(listOf("c"), started.toList())
        assertEquals(listOf(TransferStopped("b")), events.ofType<TransferStopped>())
    }

    @Test
    fun `stopping an in-flight transfer interrupts it and publishes only TransferStopped`() {
        val queue = queue(blockingDownloader, CONCURRENT_LIMIT)
        queue.enqueue("a")
        nextStart()

        queue.stop("a")

        assertTrue(interrupted.await(TIMEOUT.toMillis(), TimeUnit.MILLISECONDS))
        queue.close()
        assertEquals(listOf<Any>(TransferStopped("a")), events.all())
    }

    @Test
    fun `a stop whose listener throws still interrupts the download`() {
        val throwingOnStop =
            ApplicationEventPublisher { event ->
                events.publishEvent(event)
                if (event is TransferStopped) throw IllegalStateException(LISTENER_FAILURE)
            }
        val queue = queue(blockingDownloader, CONCURRENT_LIMIT, throwingOnStop)
        queue.enqueue("a")
        nextStart()

        assertThrows<IllegalStateException> { queue.stop("a") }

        assertTrue(interrupted.await(TIMEOUT.toMillis(), TimeUnit.MILLISECONDS))
    }

    @Test
    fun `closing the queue interrupts downloads without reporting them as failed`() {
        // An interrupted file write surfaces as ClosedByInterruptException, not InterruptedException.
        val queue =
            queue({ path, progress ->
                if (path == "failing") throw IOException(FAILURE_MESSAGE)
                try {
                    blockingDownloader.download(path, progress)
                } catch (e: InterruptedException) {
                    throw ClosedByInterruptException().apply { initCause(e) }
                }
            }, CONCURRENT_LIMIT)
        queue.enqueue("failing")
        events.await<TransferFailed>(TIMEOUT)
        queue.enqueue("a")
        nextStart()

        queue.close()

        assertTrue(interrupted.await(TIMEOUT.toMillis(), TimeUnit.MILLISECONDS))
        assertEquals(listOf<Any>(TransferFailed("failing", FAILURE_MESSAGE)), events.all())
    }

    @Test
    fun `a failing download publishes TransferFailed`() {
        val queue = queue({ _, _ -> throw IOException(FAILURE_MESSAGE) }, CONCURRENT_LIMIT)

        queue.enqueue("a")

        assertEquals(TransferFailed("a", FAILURE_MESSAGE), events.await<TransferFailed>(TIMEOUT))
    }

    @Test
    fun `paths are normalized and must stay under the remote root`() {
        val queue = queue({ _, _ -> }, CONCURRENT_LIMIT)

        listOf("", "   ", "/", "/etc/passwd", "../outside", "show/../../outside", "./show").forEach { path ->
            assertThrows<IllegalArgumentException>(path) { queue.enqueue(path) }
        }
        queue.enqueue("show//season-1/")
        assertEquals(TransferCompleted("show/season-1"), events.await<TransferCompleted>(TIMEOUT))
    }

    private fun nextStart(): String =
        checkNotNull(started.poll(TIMEOUT.toMillis(), TimeUnit.MILLISECONDS)) { "No download started within $TIMEOUT" }

    private fun queue(
        downloader: Downloader,
        concurrentLimit: Int,
        publisher: ApplicationEventPublisher = events,
    ): TransferQueue {
        val settings =
            SettingsStore(MockEnvironment(), ApplicationEventPublisher { }, directory.resolve(SETTINGS_FILE_NAME))
        settings.update { it.copy(transfer = it.transfer.copy(concurrentFileLimit = concurrentLimit)) }
        return TransferQueue(settings, publisher, downloader).also { queue = it }
    }
}
