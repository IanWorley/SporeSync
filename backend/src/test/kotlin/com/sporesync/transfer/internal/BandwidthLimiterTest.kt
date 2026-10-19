package com.sporesync.transfer.internal

import org.junit.jupiter.api.Assertions.assertEquals
import org.junit.jupiter.api.Assertions.assertTrue
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
        assertEquals(2 * halfSecond, ticker.sleeps.sum())
        assertTrue(ticker.sleeps.all { it <= TimeUnit.MILLISECONDS.toNanos(100) })
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
    fun `disabling the cap during a wait releases the waiting chunk`() {
        var rate: Long? = BYTES_PER_SECOND
        val changingTicker =
            object : Ticker {
                var now = 0L
                val sleeps = mutableListOf<Long>()

                override fun nanoTime(): Long = now

                override fun sleep(nanos: Long) {
                    sleeps += nanos
                    now += nanos
                    rate = null
                }
            }
        val limiter = BandwidthLimiter({ rate }, changingTicker)
        limiter.acquire(HALF_A_SECOND_OF_BYTES)

        limiter.acquire(HALF_A_SECOND_OF_BYTES)

        assertEquals(1, changingTicker.sleeps.size)
        assertTrue(changingTicker.sleeps.single() <= TimeUnit.MILLISECONDS.toNanos(100))
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

        assertTrue(interruptedTicker.sleeps.first() <= TimeUnit.MILLISECONDS.toNanos(100))
        assertEquals(halfSecond, interruptedTicker.sleeps.drop(1).sum())
    }
}
