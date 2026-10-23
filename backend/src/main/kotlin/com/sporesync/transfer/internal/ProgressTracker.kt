package com.sporesync.transfer.internal

import com.sporesync.transfer.TransferProgress
import java.time.Duration
import java.util.concurrent.TimeUnit

private const val PROGRESS_INTERVAL_MILLIS = 500L

/** How often progress is published per transfer, so a fast download does not flood the event bus. */
internal val PROGRESS_INTERVAL: Duration = Duration.ofMillis(PROGRESS_INTERVAL_MILLIS)

private val NANOS_PER_SECOND = TimeUnit.SECONDS.toNanos(1)

/**
 * Counts bytes for one transfer and publishes [TransferProgress] at most once per [PROGRESS_INTERVAL]. Used by the
 * single worker thread running that transfer, so it needs no locking.
 */
internal class ProgressTracker(
    private val path: String,
    private val publish: (TransferProgress) -> Unit,
    private val ticker: Ticker = SystemTicker,
) {
    private var totalBytes = 0L
    private var transferred = 0L
    private var lastPublishNanos = 0L
    private var lastPublishBytes = 0L

    /** Publishes right away, so listeners learn the total and how much was resumed before any bytes move. */
    fun start(
        totalBytes: Long,
        alreadyTransferred: Long,
    ) {
        this.totalBytes = totalBytes
        transferred = alreadyTransferred
        lastPublishNanos = ticker.nanoTime()
        lastPublishBytes = alreadyTransferred
        publish(TransferProgress(path, transferred, totalBytes, bytesPerSecond = 0, eta = null))
    }

    /** Corrects the planned resume total when a partial file changed before its download began. */
    fun reconcileResume(
        plannedOffset: Long,
        actualOffset: Long,
    ) {
        if (plannedOffset == actualOffset) return
        transferred += actualOffset - plannedOffset
        lastPublishNanos = ticker.nanoTime()
        lastPublishBytes = transferred
        publish(TransferProgress(path, transferred, totalBytes, bytesPerSecond = 0, eta = null))
    }

    fun advance(bytes: Int) {
        transferred += bytes
        val now = ticker.nanoTime()
        val elapsed = now - lastPublishNanos
        if (elapsed < PROGRESS_INTERVAL.toNanos()) return

        val speed = (transferred - lastPublishBytes) * NANOS_PER_SECOND / elapsed
        val eta = if (speed > 0) Duration.ofSeconds((totalBytes - transferred) / speed) else null
        publish(TransferProgress(path, transferred, totalBytes, speed, eta))
        lastPublishNanos = now
        lastPublishBytes = transferred
    }
}
