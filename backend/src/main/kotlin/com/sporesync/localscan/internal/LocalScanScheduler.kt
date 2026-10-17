package com.sporesync.localscan.internal

import com.sporesync.config.ConfigChanged
import com.sporesync.config.SettingsStore
import com.sporesync.localscan.LocalScanned
import jakarta.annotation.PreDestroy
import org.slf4j.LoggerFactory
import org.springframework.boot.context.event.ApplicationReadyEvent
import org.springframework.context.ApplicationEventPublisher
import org.springframework.context.event.EventListener
import org.springframework.stereotype.Component
import java.io.IOException
import java.time.Duration
import java.util.concurrent.Executors
import java.util.concurrent.ScheduledFuture
import java.util.concurrent.TimeUnit

private const val THREAD_NAME = "localscan"
private const val NO_INITIAL_DELAY = 0L

// The longest delay the executor accepts. Config allows any positive interval, and anything longer means "never
// again" in practice, so longer intervals are capped instead of overflowing and leaving the scan unscheduled.
private val MAX_INTERVAL = Duration.ofNanos(Long.MAX_VALUE)

/**
 * Scans the configured local directory every Scan > local interval and publishes [LocalScanned]. The directory is
 * read from the settings on every run, and an interval change reschedules the scan without a restart.
 */
@Component
internal class LocalScanScheduler(
    private val settings: SettingsStore,
    private val events: ApplicationEventPublisher,
) {
    private val log = LoggerFactory.getLogger(LocalScanScheduler::class.java)

    // A dedicated thread, so walking a large tree never delays the remote scan or any other scheduled work.
    // Fixed-delay scheduling on one thread also means scans never overlap.
    private val executor =
        Executors.newSingleThreadScheduledExecutor(
            Thread
                .ofPlatform()
                .name(THREAD_NAME)
                .daemon()
                .factory(),
        )

    private var scheduled: ScheduledFuture<*>? = null

    @EventListener(ApplicationReadyEvent::class)
    @Synchronized
    fun start() = schedule(settings.current.scan.localInterval)

    @EventListener
    @Synchronized
    fun on(event: ConfigChanged) {
        val interval = event.current.scan.localInterval
        if (scheduled != null && interval != event.previous.scan.localInterval) schedule(interval)
    }

    @PreDestroy
    fun stop() {
        executor.shutdownNow()
    }

    private fun schedule(interval: Duration) {
        scheduled?.cancel(false)
        val delay = minOf(interval, MAX_INTERVAL).toNanos()
        scheduled = executor.scheduleWithFixedDelay(::scan, NO_INITIAL_DELAY, delay, TimeUnit.NANOSECONDS)
        log.debug("Scanning the local directory every {}", interval)
    }

    // An exception escaping a run would silently cancel every later run, so failures are logged and retried next run.
    @Suppress("TooGenericExceptionCaught")
    private fun scan() {
        val root = settings.current.paths.localDir
        try {
            events.publishEvent(LocalScanned(root, LocalScanner.scan(root)))
        } catch (e: IOException) {
            log.warn("Cannot scan local directory {}: {}", root, e.toString())
        } catch (e: RuntimeException) {
            log.error("Local scan of {} failed", root, e)
        }
    }
}
