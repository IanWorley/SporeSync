package com.sporesync.transfer.internal

import org.junit.jupiter.api.Assertions.assertEquals
import org.junit.jupiter.api.Test
import org.junit.jupiter.api.assertThrows
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

    @Test
    fun `changing the rate discards reservations from the previous rate`() {
        var rate: Long? = BYTES_PER_SECOND
        val limiter = BandwidthLimiter({ rate }, ticker)

        limiter.acquire(HALF_A_SECOND_OF_BYTES)
        rate = null
        limiter.acquire(HALF_A_SECOND_OF_BYTES)
        rate = BYTES_PER_SECOND
        limiter.acquire(HALF_A_SECOND_OF_BYTES)

        assertEquals(emptyList<Long>(), ticker.sleeps)
    }

    @Test
    fun `an interrupted wait returns its unused reservation`() {
        val interruptedTicker =
            object : Ticker {
                var now = 0L
                var interruptNextSleep = true
                val sleeps = mutableListOf<Long>()

                override fun nanoTime(): Long = now

                override fun sleep(nanos: Long) {
                    sleeps += nanos
                    if (interruptNextSleep) {
                        interruptNextSleep = false
                        throw InterruptedException()
                    }
                    now += nanos
                }
            }
        val limiter = BandwidthLimiter({ BYTES_PER_SECOND }, interruptedTicker)
        val halfSecond = TimeUnit.MILLISECONDS.toNanos(500)

        limiter.acquire(HALF_A_SECOND_OF_BYTES)
        assertThrows<InterruptedException> { limiter.acquire(HALF_A_SECOND_OF_BYTES) }
        limiter.acquire(HALF_A_SECOND_OF_BYTES)

        assertEquals(listOf(halfSecond, halfSecond), interruptedTicker.sleeps)
    }
}
