package com.sporesync.transfer

import com.sporesync.config.ConfigChanged
import com.sporesync.config.SettingsStore
import com.sporesync.transfer.internal.Downloader
import com.sporesync.transfer.internal.ProgressTracker
import org.slf4j.LoggerFactory
import org.springframework.context.ApplicationEventPublisher
import org.springframework.context.event.EventListener
import org.springframework.stereotype.Service
import java.time.Duration
import java.util.concurrent.ConcurrentHashMap
import java.util.concurrent.LinkedBlockingQueue
import java.util.concurrent.ThreadPoolExecutor
import java.util.concurrent.TimeUnit
import java.util.concurrent.atomic.AtomicInteger

private const val PATH_SEPARATOR = "/"
private const val THREAD_NAME_PREFIX = "transfer-"
private const val SHUTDOWN_TIMEOUT_SECONDS = 10L
private val SHUTDOWN_TIMEOUT: Duration = Duration.ofSeconds(SHUTDOWN_TIMEOUT_SECONDS)
private val ILLEGAL_SEGMENTS = setOf(".", "..")

/**
 * The `transfer` module's API: a download queue running up to `transfer.concurrent-file-limit` transfers at once.
 * Each queued path, a file or a whole directory, takes one slot. Every enqueued path ends with exactly one of
 * [TransferCompleted], [TransferFailed], or [TransferStopped], and no [TransferProgress] follows it.
 */
@Service
class TransferQueue internal constructor(
    settings: SettingsStore,
    private val events: ApplicationEventPublisher,
    private val downloader: Downloader,
) : AutoCloseable {
    private val log = LoggerFactory.getLogger(TransferQueue::class.java)

    /** Queued and in-flight transfers by normalized path. A path is removed once no worker can touch its files. */
    private val transfers = ConcurrentHashMap<String, Transfer>()
    private val lifecycle = Any()

    private val executor: ThreadPoolExecutor =
        settings.current.transfer.concurrentFileLimit.let { limit ->
            val threadCount = AtomicInteger()
            ThreadPoolExecutor(limit, limit, 0, TimeUnit.MILLISECONDS, LinkedBlockingQueue()) { task ->
                Thread(task, THREAD_NAME_PREFIX + threadCount.incrementAndGet())
            }
        }

    /**
     * Queues [path], relative to the remote root, for download. Does nothing while the path is queued, downloading,
     * or still unwinding from a stop. Throws [IllegalArgumentException] for a blank or absolute path, or one with
     * `.` or `..` segments.
     */
    fun enqueue(path: String) {
        val transfer = Transfer(normalize(path))
        synchronized(transfers) {
            if (transfers.keys.none { pathsOverlap(it, transfer.path) }) {
                transfers[transfer.path] = transfer
                executor.execute { run(transfer) }
            }
        }
    }

    /** Stops [path] whether queued or in flight, and publishes [TransferStopped]. Partial files are kept. */
    fun stop(path: String) {
        transfers[normalize(path)]?.stop()
    }

    /** Applies a new concurrent limit. Transfers already running past a lowered limit are allowed to finish. */
    @EventListener
    fun on(event: ConfigChanged) {
        val limit = event.current.transfer.concurrentFileLimit
        synchronized(executor) {
            // The core size may never exceed the maximum, so the order depends on the direction of the change.
            if (limit > executor.maximumPoolSize) {
                executor.maximumPoolSize = limit
                executor.corePoolSize = limit
            } else {
                executor.corePoolSize = limit
                executor.maximumPoolSize = limit
            }
        }
    }

    /** Interrupts every transfer without publishing events; partial files stay on disk for the next run. */
    override fun close() {
        synchronized(lifecycle) {
            executor.shutdownNow()
        }
        executor.awaitTermination(SHUTDOWN_TIMEOUT.toMillis(), TimeUnit.MILLISECONDS)
    }

    // Any failure must still end the transfer with an event, or the model would show it downloading forever.
    @Suppress("TooGenericExceptionCaught")
    private fun run(transfer: Transfer) {
        if (!transfer.begin()) return
        try {
            downloader.download(transfer.path, ProgressTracker(transfer.path, transfer::publish))
            transfer.finish(TransferCompleted(transfer.path))
        } catch (e: Exception) {
            // A shutdown interrupt can surface as ClosedByInterruptException, not only InterruptedException.
            if (executor.isShutdown) {
                log.debug("Transfer of {} interrupted by shutdown", transfer.path, e)
            } else if (transfer.finish(TransferFailed(transfer.path, e.message ?: e.javaClass.simpleName))) {
                log.warn("Transfer of {} failed", transfer.path, e)
            }
        } finally {
            transfer.end()
        }
    }

    /**
     * One queued path. Its lock makes the terminal event happen exactly once, keeps progress from racing past it,
     * and makes sure a stop only ever interrupts the worker while it is still running this transfer.
     */
    private inner class Transfer(
        val path: String,
    ) {
        private var finished = false

        // Separate from the event lock so stop can interrupt a listener holding that lock.
        private val workerOwnership = Any()
        private var worker: Thread? = null

        @Volatile
        private var stopRequested = false

        /** Claims the transfer for the current worker thread. False when it was stopped while still queued. */
        fun begin(): Boolean =
            synchronized(this) {
                if (!finished) {
                    synchronized(workerOwnership) { worker = Thread.currentThread() }
                }
                !finished
            }

        fun publish(progress: TransferProgress) =
            synchronized(this) {
                if (!finished) events.publishEvent(progress)
            }

        /** Publishes [event] unless the transfer already ended. Returns whether this call ended it. */
        fun finish(event: Any): Boolean {
            val shouldPublish =
                synchronized(this) {
                    if (finished || (stopRequested && event !is TransferStopped)) {
                        false
                    } else {
                        synchronized(lifecycle) {
                            finished = true
                            !executor.isShutdown
                        }
                    }
                }
            if (shouldPublish) events.publishEvent(event)
            return shouldPublish
        }

        fun stop() {
            stopRequested = true
            synchronized(workerOwnership) { worker?.interrupt() }
            synchronized(this) {
                if (finished) return
                val running = synchronized(workerOwnership) { worker }
                if (running != null) {
                    running.interrupt()
                } else {
                    // Still queued: no worker will reach end(), so free the path after its stop event.
                    try {
                        finish(TransferStopped(path))
                    } finally {
                        synchronized(transfers) { transfers.remove(path, this) }
                    }
                    return
                }
                finish(TransferStopped(path))
            }
        }

        /** Frees the path once the worker is done with it, so a new enqueue never shares files with this one. */
        fun end() {
            synchronized(this) {
                synchronized(workerOwnership) {
                    worker = null
                    // Clear the final stop interrupt before this worker can start another transfer.
                    Thread.interrupted()
                }
                synchronized(transfers) { transfers.remove(path, this) }
            }
        }
    }

    private companion object {
        fun normalize(path: String): String {
            val segments = path.split(PATH_SEPARATOR).filter { it.isNotEmpty() }
            require(!path.isBlank() && !path.startsWith(PATH_SEPARATOR) && segments.isNotEmpty()) {
                "Transfer path must be relative to the remote root: $path"
            }
            require(segments.none { it in ILLEGAL_SEGMENTS }) { "Transfer path must not contain . or ..: $path" }
            return segments.joinToString(PATH_SEPARATOR)
        }

        private fun pathsOverlap(
            first: String,
            second: String,
        ): Boolean =
            first == second || first.startsWith("$second$PATH_SEPARATOR") || second.startsWith("$first$PATH_SEPARATOR")
    }
}
