package com.sporesync.transfer.internal

import org.junit.jupiter.api.Assertions.assertEquals
import org.junit.jupiter.api.Test
import java.util.concurrent.TimeUnit

private const val BYTES_PER_SECOND = 1_000L
private const val HALF_A_SECOND_OF_BYTES = 500

class BandwidthLimiterTest {
    private val ticker = FakeTicker()

    @Test
    fun `chunks are paced to the configured rate`() {
        val limiter = BandwidthLimiter({ BYTES_PER_SECOND }, ticker)

        repeat(3) { limiter.acquire(HALF_A_SECOND_OF_BYTES) }

        val halfSecond = TimeUnit.MILLISECONDS.toNanos(500)
        assertEquals(listOf(halfSecond, halfSecond), ticker.sleeps)
    }

    @Test
    fun `no limit and a zero limit never wait`() {
        BandwidthLimiter({ null }, ticker).acquire(HALF_A_SECOND_OF_BYTES)
        BandwidthLimiter({ 0L }, ticker).acquire(HALF_A_SECOND_OF_BYTES)

        assertEquals(emptyList<Long>(), ticker.sleeps)
    }
}
