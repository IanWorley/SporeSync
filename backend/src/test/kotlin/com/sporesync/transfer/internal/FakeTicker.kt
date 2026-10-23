package com.sporesync.transfer.internal

/** A clock that only moves when told to; sleeping advances it and records the request. */
internal class FakeTicker : Ticker {
    var now = 0L
    val sleeps = mutableListOf<Long>()

    override fun nanoTime(): Long = now

    override fun sleep(nanos: Long) {
        sleeps += nanos
        now += nanos
    }
}
