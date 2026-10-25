package com.sporesync.transfer.internal

import com.sporesync.transfer.TransferProgress
import org.junit.jupiter.api.Assertions.assertEquals
import org.junit.jupiter.api.Test
import java.time.Duration

private const val PATH = "show"
private const val TOTAL_BYTES = 10_000L
private const val RESUMED_BYTES = 2_000L
private const val CHUNK = 500
private const val BYTES_IN_ONE_SECOND = 2_000

class ProgressTrackerTest {
    private val ticker = FakeTicker()
    private val published = mutableListOf<TransferProgress>()
    private val tracker = ProgressTracker(PATH, published::add, ticker)

    @Test
    fun `publishes on start and then at most once per interval`() {
        tracker.start(TOTAL_BYTES, RESUMED_BYTES)
        repeat(3) { tracker.advance(CHUNK) }

        ticker.now += PROGRESS_INTERVAL.toNanos()
        tracker.advance(CHUNK)

        assertEquals(listOf(RESUMED_BYTES, RESUMED_BYTES + 4 * CHUNK), published.map { it.bytesTransferred })
    }

    @Test
    fun `speed counts bytes since the last event and ETA covers what remains`() {
        tracker.start(TOTAL_BYTES, RESUMED_BYTES)

        ticker.now += Duration.ofSeconds(1).toNanos()
        tracker.advance(BYTES_IN_ONE_SECOND)

        // 2,000 bytes per second with 6,000 bytes left: three seconds.
        val transferred = RESUMED_BYTES + BYTES_IN_ONE_SECOND
        val eta = Duration.ofSeconds((TOTAL_BYTES - transferred) / BYTES_IN_ONE_SECOND)
        assertEquals(
            TransferProgress(PATH, transferred, TOTAL_BYTES, BYTES_IN_ONE_SECOND.toLong(), eta),
            published.last(),
        )
    }

    @Test
    fun `corrects resumed bytes when the partial file changed after planning`() {
        tracker.start(TOTAL_BYTES, RESUMED_BYTES)

        tracker.reconcileResume(RESUMED_BYTES, RESUMED_BYTES / 2)

        assertEquals(RESUMED_BYTES / 2, published.last().bytesTransferred)
        assertEquals(0L, published.last().bytesPerSecond)
    }
}
