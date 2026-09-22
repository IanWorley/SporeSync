package dev.sporesync.model.download

import dev.sporesync.model.inventory.EntryType
import dev.sporesync.model.inventory.InventoryEntry
import dev.sporesync.model.settings.DownloadSettings
import java.io.IOException
import java.nio.file.Files
import java.nio.file.Path
import java.security.MessageDigest
import java.sql.ResultSet
import org.springframework.jdbc.core.JdbcTemplate
import org.springframework.stereotype.Component
import org.springframework.transaction.PlatformTransactionManager
import org.springframework.transaction.support.TransactionTemplate
import tools.jackson.databind.json.JsonMapper

const val MAX_DOWNLOAD_ATTEMPTS = 3
private const val JOB_MUTATION_LOCK_ID = 1397772115L

enum class JobState {
  QUEUED,
  RUNNING,
  COMPLETE,
  FAILED,
  CANCELLED,
  PAUSED,
  REMOTE_DELETED,
}

enum class JobAction {
  PAUSE,
  DELETE_LOCAL,
  DELETE_REMOTE,
}

data class DownloadSpec(val settings: DownloadSettings, val entry: InventoryEntry) {
  fun identity(): String =
      MessageDigest.getInstance("SHA-256")
          .digest(
              listOf(
                      settings.host,
                      settings.port,
                      settings.username,
                      settings.source,
                      settings.destination,
                      entry.path,
                      entry.sizeBytes,
                      entry.modifiedTimeNs,
                  )
                  .joinToString("\u0000")
                  .toByteArray()
          )
          .toHexString()
}

data class DownloadJob(
    val id: String,
    val spec: DownloadSpec,
    val state: JobState,
    val bytesDone: Long,
    val attempts: Int,
    val error: String?,
    val cancelRequested: Boolean,
    val action: JobAction? = null,
)

@Component
class DownloadJobs(
    private val jdbc: JdbcTemplate,
    private val mapper: JsonMapper,
    transactionManager: PlatformTransactionManager,
) : DownloadJobRepository {
  private val transactions = TransactionTemplate(transactionManager)

  override fun enqueue(spec: DownloadSpec): DownloadJob =
      requireNotNull(
          transactions.execute {
            spec.settings.validate()
            require(spec.entry.type == EntryType.file)
            mutationLock()
            val id = spec.identity()
            find(id)?.let {
              return@execute it
            }
            val deleted =
                jdbc
                    .query("SELECT * FROM download_jobs WHERE state = 'REMOTE_DELETED'", ::decode)
                    .any { sameRemote(it.spec, spec) && it.spec.entry == spec.entry }
            jdbc.update(
                "INSERT INTO download_jobs (id, spec, state) VALUES (?, ?, ?) ON CONFLICT DO NOTHING",
                id,
                mapper.writeValueAsString(spec),
                if (deleted) JobState.REMOTE_DELETED.name else JobState.QUEUED.name,
            )
            requireNotNull(find(id))
          }
      )

  override fun list(): List<DownloadJob> =
      jdbc.query("SELECT * FROM download_jobs ORDER BY created_at DESC", ::decode)

  override fun find(id: String): DownloadJob? =
      jdbc.query("SELECT * FROM download_jobs WHERE id = ?", ::decode, id).firstOrNull()

  /** Call only while holding the worker lock; RUNNING rows belong to an interrupted worker. */
  override fun recover() {
    jdbc.update(
        "UPDATE download_jobs SET state = CASE WHEN cancel_requested THEN 'CANCELLED' WHEN attempts >= ? THEN 'FAILED' ELSE 'QUEUED' END, error = 'INTERRUPTED', updated_at = now() WHERE state = 'RUNNING' AND action IS NULL",
        MAX_DOWNLOAD_ATTEMPTS,
    )
  }

  override fun next(): DownloadJob? =
      jdbc
          .query(
              "SELECT * FROM download_jobs WHERE state = 'QUEUED' OR action IS NOT NULL ORDER BY (action IS NOT NULL) DESC, created_at LIMIT 1",
              ::decode,
          )
          .firstOrNull()

  override fun start(id: String): Boolean =
      jdbc.update(
          "UPDATE download_jobs SET state = 'RUNNING', attempts = attempts + 1, error = NULL, updated_at = now() WHERE id = ? AND state = 'QUEUED' AND action IS NULL",
          id,
      ) == 1

  override fun progress(id: String, bytes: Long) {
    jdbc.update(
        "UPDATE download_jobs SET bytes_done = ?, updated_at = now() WHERE id = ?",
        bytes,
        id,
    )
  }

  override fun finish(id: String, state: JobState, error: String?) {
    jdbc.update(
        "UPDATE download_jobs SET state = CASE WHEN cancel_requested THEN 'CANCELLED' ELSE ? END, error = ?, updated_at = now() WHERE id = ? AND action IS NULL",
        state.name,
        error,
        id,
    )
  }

  override fun cancel(id: String): DownloadJob? {
    jdbc.update(
        "UPDATE download_jobs SET cancel_requested = true, state = CASE WHEN state IN ('QUEUED','PAUSED') THEN 'CANCELLED' ELSE state END, updated_at = now() WHERE id = ? AND state IN ('QUEUED','RUNNING','PAUSED') AND action IS NULL",
        id,
    )
    return find(id)
  }

  override fun retry(id: String): DownloadJob? {
    jdbc.update(
        "UPDATE download_jobs SET state = 'QUEUED', attempts = 0, cancel_requested = false, error = NULL, updated_at = now() WHERE id = ? AND state IN ('FAILED','CANCELLED') AND action IS NULL",
        id,
    )
    return find(id)
  }

  override fun requestAction(id: String, action: JobAction): DownloadJob? {
    val allowed = if (action == JobAction.PAUSE) "AND state IN ('QUEUED','RUNNING')" else ""
    jdbc.update(
        "UPDATE download_jobs SET action = ?, error = NULL, updated_at = now() WHERE id = ? AND action IS NULL $allowed",
        action.name,
        id,
    )
    return find(id)
  }

  override fun resume(id: String): DownloadJob? {
    jdbc.update(
        "UPDATE download_jobs SET state = 'QUEUED', attempts = 0, cancel_requested = false, error = NULL, updated_at = now() WHERE id = ? AND state = 'PAUSED' AND action IS NULL",
        id,
    )
    return find(id)
  }

  override fun requeueMissing(id: String) {
    jdbc.update(
        "UPDATE download_jobs SET state = 'QUEUED', bytes_done = 0, attempts = 0, error = NULL, updated_at = now() WHERE id = ? AND state = 'COMPLETE' AND action IS NULL",
        id,
    )
  }

  override fun completeAction(job: DownloadJob, error: String?) =
      transactions.executeWithoutResult {
        mutationLock()
        val action = requireNotNull(job.action)
        if (error != null) {
          jdbc.update(
              "UPDATE download_jobs SET state = CASE WHEN state IN ('QUEUED','RUNNING') THEN 'FAILED' ELSE state END, action = NULL, error = ?, updated_at = now() WHERE id = ? AND action = ?",
              error,
              job.id,
              action.name,
          )
          return@executeWithoutResult
        }
        if (action != JobAction.PAUSE) {
          val spec = job.spec
          for (related in list()) {
            if (related.id == job.id) continue
            val sharesFile =
                if (action == JobAction.DELETE_LOCAL)
                    sameDestination(related.spec.settings.destination, spec.settings.destination) &&
                        related.spec.entry.path == spec.entry.path
                else sameRemote(related.spec, spec)
            if (!sharesFile) continue
            jdbc.update(
                "UPDATE download_jobs SET state = ?, action = CASE WHEN action = 'PAUSE' THEN NULL ELSE action END, bytes_done = CASE WHEN ? THEN 0 ELSE bytes_done END, error = ?, updated_at = now() WHERE id = ?",
                if (action == JobAction.DELETE_REMOTE) JobState.REMOTE_DELETED.name
                else if (related.state == JobState.REMOTE_DELETED) related.state.name
                else JobState.CANCELLED.name,
                action == JobAction.DELETE_LOCAL,
                if (action == JobAction.DELETE_LOCAL) "LOCAL_DELETED" else null,
                related.id,
            )
          }
        }
        val state =
            when (action) {
              JobAction.PAUSE -> JobState.PAUSED
              JobAction.DELETE_LOCAL ->
                  if (job.state == JobState.REMOTE_DELETED) JobState.REMOTE_DELETED
                  else JobState.QUEUED
              JobAction.DELETE_REMOTE -> JobState.REMOTE_DELETED
            }
        jdbc.update(
            "UPDATE download_jobs SET state = ?, action = NULL, cancel_requested = false, bytes_done = CASE WHEN ? THEN 0 ELSE bytes_done END, attempts = 0, error = NULL, updated_at = now() WHERE id = ? AND action = ?",
            state.name,
            action == JobAction.DELETE_LOCAL,
            job.id,
            action.name,
        )
      }

  private fun sameDestination(left: String, right: String): Boolean =
      left == right ||
          try {
            Files.isSameFile(Path.of(left), Path.of(right))
          } catch (_: IOException) {
            false
          }

  private fun mutationLock() {
    jdbc.execute("SELECT pg_advisory_xact_lock($JOB_MUTATION_LOCK_ID)")
  }

  private fun sameRemote(left: DownloadSpec, right: DownloadSpec): Boolean =
      left.settings.host == right.settings.host &&
          left.settings.port == right.settings.port &&
          left.settings.username == right.settings.username &&
          left.settings.source == right.settings.source &&
          left.entry.path == right.entry.path

  private fun decode(row: ResultSet, @Suppress("UNUSED_PARAMETER") index: Int) =
      DownloadJob(
          row.getString("id"),
          mapper.readValue(row.getString("spec"), DownloadSpec::class.java),
          JobState.valueOf(row.getString("state")),
          row.getLong("bytes_done"),
          row.getInt("attempts"),
          row.getString("error"),
          row.getBoolean("cancel_requested"),
          row.getString("action")?.let(JobAction::valueOf),
      )
}
