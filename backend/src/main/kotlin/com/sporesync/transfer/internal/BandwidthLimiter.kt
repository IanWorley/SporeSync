package com.sporesync.transfer.internal

import java.util.concurrent.TimeUnit
import java.util.concurrent.locks.ReentrantLock

private val NANOS_PER_SECOND = TimeUnit.SECONDS.toNanos(1)
private const val RATE_RECHECK_INTERVAL_MILLIS = 100L
private val RATE_RECHECK_INTERVAL_NANOS = TimeUnit.MILLISECONDS.toNanos(RATE_RECHECK_INTERVAL_MILLIS)

/**
 * One global byte-rate cap shared by every transfer. Each caller reserves the next free slot on a single timeline
 * and sleeps until it starts, so concurrent transfers split the cap between them. Idle time earns no burst credit.
 *
 * [bytesPerSecond] is read on every call, so a settings change applies to the next chunk. `null` or zero means
 * unlimited.
 */
internal class BandwidthLimiter(
    private val bytesPerSecond: () -> Long?,
    private val ticker: Ticker = SystemTicker,
) {
    private val lock = ReentrantLock()

    // Long.MIN_VALUE rather than zero: System.nanoTime() may be negative.
    private var nextFreeNanos = Long.MIN_VALUE
    private var activeRate: Long? = null

    /** Blocks until [bytes] fit under the cap. Interruptible, so stopping a transfer never waits out a slot. */
    fun acquire(bytes: Int) {
        lock.lockInterruptibly()
        try {
            acquireLocked(bytes)
        } finally {
            lock.unlock()
        }
    }

    private fun acquireLocked(bytes: Int) {
        val rate = currentRate()
        val now = ticker.nanoTime()
        if (rate != activeRate) {
            activeRate = rate
            nextFreeNanos = now
        }
        if (rate == null) return

        val start = maxOf(nextFreeNanos, now)
        val reservationEnd = start + bytes * NANOS_PER_SECOND / rate
        nextFreeNanos = reservationEnd
        waitForReservation(bytes, rate, start, reservationEnd)
    }

    private fun waitForReservation(
        bytes: Int,
        reservedRate: Long,
        start: Long,
        reservationEnd: Long,
    ) {
        var remaining = start - ticker.nanoTime()
        while (remaining > 0) {
            try {
                ticker.sleep(minOf(remaining, RATE_RECHECK_INTERVAL_NANOS))
            } catch (e: InterruptedException) {
                if (nextFreeNanos == reservationEnd) nextFreeNanos = start
                throw e
            }
            remaining = recheckRate(bytes, reservedRate, start)
        }
    }

    private fun recheckRate(
        bytes: Int,
        reservedRate: Long,
        start: Long,
    ): Long {
        val currentRate = currentRate()
        val currentTime = ticker.nanoTime()
        if (currentRate != reservedRate) {
            activeRate = currentRate
            nextFreeNanos = currentTime
            if (currentRate != null) {
                nextFreeNanos = currentTime + bytes * NANOS_PER_SECOND / currentRate
            }
        }
        return if (currentRate != reservedRate) 0 else (start - currentTime).coerceAtLeast(0)
    }

    private fun currentRate(): Long? = bytesPerSecond()?.takeIf { it > 0 }
}
