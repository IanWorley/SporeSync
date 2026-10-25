package com.sporesync.transfer.internal

import java.util.concurrent.TimeUnit

/** Monotonic time and sleeping, behind an interface so throttling and rate limiting can be tested without waiting. */
internal interface Ticker {
    fun nanoTime(): Long

    fun sleep(nanos: Long)
}

internal object SystemTicker : Ticker {
    override fun nanoTime(): Long = System.nanoTime()

    override fun sleep(nanos: Long) = TimeUnit.NANOSECONDS.sleep(nanos)
}
