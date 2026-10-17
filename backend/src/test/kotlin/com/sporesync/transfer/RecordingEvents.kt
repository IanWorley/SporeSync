package com.sporesync.transfer

import org.springframework.context.ApplicationEventPublisher
import java.time.Duration
import java.util.concurrent.CopyOnWriteArrayList
import java.util.concurrent.TimeUnit
import java.util.concurrent.locks.ReentrantLock
import kotlin.concurrent.withLock

/** Records events published from worker threads and lets a test wait for one to arrive. */
class RecordingEvents : ApplicationEventPublisher {
    private val events = CopyOnWriteArrayList<Any>()
    private val lock = ReentrantLock()
    private val published = lock.newCondition()

    override fun publishEvent(event: Any) {
        events += event
        lock.withLock { published.signalAll() }
    }

    inline fun <reified T> ofType(): List<T> = all().filterIsInstance<T>()

    fun all(): List<Any> = events.toList()

    /** Waits for the first event of type [T] matching [predicate]; fails the test after [timeout]. */
    inline fun <reified T> await(
        timeout: Duration,
        crossinline predicate: (T) -> Boolean = { true },
    ): T = awaitAny(timeout, T::class.java.simpleName) { event -> event is T && predicate(event) } as T

    fun awaitAny(
        timeout: Duration,
        description: String,
        matches: (Any) -> Boolean,
    ): Any {
        val deadline = System.nanoTime() + timeout.toNanos()
        lock.withLock {
            while (true) {
                events.firstOrNull(matches)?.let { return it }
                val remaining = deadline - System.nanoTime()
                check(remaining > 0) { "No matching $description within $timeout; saw ${all()}" }
                published.await(remaining, TimeUnit.NANOSECONDS)
            }
        }
    }
}
