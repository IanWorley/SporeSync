package com.sporesync.transfer.internal

import java.util.concurrent.TimeUnit

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
    // Long.MIN_VALUE rather than zero: System.nanoTime() may be negative.
    private var nextFreeNanos = Long.MIN_VALUE

    /** Blocks until [bytes] fit under the cap. Interruptible, so stopping a transfer never waits out a slot. */
    fun acquire(bytes: Int) {
        val rate = bytesPerSecond()?.takeIf { it > 0 } ?: return
        val wait =
            synchronized(this) {
                val now = ticker.nanoTime()
                val start = maxOf(nextFreeNanos, now)
                nextFreeNanos = start + bytes * NANOS_PER_SECOND / rate
                start - now
            }
        if (wait > 0) ticker.sleep(wait)
    }
}
