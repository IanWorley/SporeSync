package dev.sporesync

import java.sql.Connection
import javax.sql.DataSource
import org.springframework.beans.factory.annotation.Value
import org.springframework.context.annotation.Configuration
import org.springframework.scheduling.annotation.EnableScheduling
import org.springframework.scheduling.annotation.Scheduled
import org.springframework.stereotype.Service

private const val WORKER_POLL_MILLIS = 1000L
internal const val WORKER_LOCK_ID =
    1397772114L // Stable PostgreSQL session lock shared by all instances.
private const val CONNECTION_CHECK_SECONDS = 1

@Configuration(proxyBeanMethods = false) @EnableScheduling class JobScheduling

@Service
class DownloadWorker(
    private val dataSource: DataSource,
    private val jobs: DownloadJobs,
    private val downloader: SftpDownload,
    @param:Value("\${sporesync.background.enabled:true}") private val enabled: Boolean,
) {
  @Scheduled(fixedDelay = WORKER_POLL_MILLIS)
  fun scheduled() {
    if (enabled) tick()
  }

  @Synchronized
  fun tick() {
    dataSource.connection.use { connection ->
      if (!lock(connection, "pg_try_advisory_lock")) return
      try {
        jobs.recover()
        val job = jobs.next() ?: return
        if (!jobs.start(job.id)) return
        try {
          downloader.transfer(job.spec, { bytes -> jobs.progress(job.id, bytes) }) {
            if (Thread.currentThread().isInterrupted) throw InterruptedException()
            check(connection.isValid(CONNECTION_CHECK_SECONDS))
            jobs.find(job.id)?.cancelRequested != false
          }
          jobs.finish(job.id, JobState.COMPLETE)
        } catch (error: Exception) {
          val code = (error as? DownloadFailure)?.code ?: "TRANSFER_FAILED"
          val state =
              when {
                code == "CANCELLED" -> JobState.CANCELLED
                error is DownloadFailure || error is IllegalArgumentException -> JobState.FAILED
                job.attempts + 1 < MAX_DOWNLOAD_ATTEMPTS -> JobState.QUEUED
                else -> JobState.FAILED
              }
          jobs.finish(job.id, state, code)
        }
      } finally {
        // Session locks must be released before returning a live connection to the pool.
        if (!connection.isClosed) lock(connection, "pg_advisory_unlock")
      }
    }
  }

  private fun lock(connection: Connection, operation: String): Boolean =
      connection.prepareStatement("SELECT $operation(?)").use { statement ->
        statement.setLong(1, WORKER_LOCK_ID)
        statement.executeQuery().use { rows ->
          rows.next()
          rows.getBoolean(1)
        }
      }
}
