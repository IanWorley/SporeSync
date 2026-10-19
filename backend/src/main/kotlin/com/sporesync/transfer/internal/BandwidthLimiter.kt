package com.sporesync.transfer.internal

import java.util.concurrent.TimeUnit
import java.util.concurrent.locks.ReentrantLock

private val NANOS_PER_SECOND = TimeUnit.SECONDS.toNanos(1)

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
            val rate = bytesPerSecond()?.takeIf { it > 0 }
            val now = ticker.nanoTime()
            if (rate != activeRate) {
                activeRate = rate
                nextFreeNanos = now
            }
            if (rate == null) return

            val start = maxOf(nextFreeNanos, now)
            val wait = start - now
            nextFreeNanos = start + bytes * NANOS_PER_SECOND / rate
            try {
                if (wait > 0) ticker.sleep(wait)
            } catch (e: InterruptedException) {
                nextFreeNanos = start
                throw e
            }
        } finally {
            lock.unlock()
        }
    }
}
