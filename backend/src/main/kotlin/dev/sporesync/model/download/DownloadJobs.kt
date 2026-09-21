package dev.sporesync.model.download

import dev.sporesync.model.inventory.EntryType
import dev.sporesync.model.inventory.InventoryEntry
import dev.sporesync.model.settings.DownloadSettings
import java.security.MessageDigest
import java.sql.ResultSet
import org.springframework.jdbc.core.JdbcTemplate
import org.springframework.stereotype.Component
import tools.jackson.databind.json.JsonMapper

const val MAX_DOWNLOAD_ATTEMPTS = 3

enum class JobState {
  QUEUED,
  RUNNING,
  COMPLETE,
  FAILED,
  CANCELLED,
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
)

@Component
class DownloadJobs(private val jdbc: JdbcTemplate, private val mapper: JsonMapper) :
    DownloadJobRepository {
  override fun enqueue(spec: DownloadSpec): DownloadJob {
    spec.settings.validate()
    require(spec.entry.type == EntryType.file)
    val id = spec.identity()
    jdbc.update(
        "INSERT INTO download_jobs (id, spec, state) VALUES (?, ?, 'QUEUED') ON CONFLICT DO NOTHING",
        id,
        mapper.writeValueAsString(spec),
    )
    return requireNotNull(find(id))
  }

  override fun list(): List<DownloadJob> =
      jdbc.query("SELECT * FROM download_jobs ORDER BY created_at DESC", ::decode)

  override fun find(id: String): DownloadJob? =
      jdbc.query("SELECT * FROM download_jobs WHERE id = ?", ::decode, id).firstOrNull()

  /** Call only while holding the worker lock; RUNNING rows belong to an interrupted worker. */
  override fun recover() {
    jdbc.update(
        "UPDATE download_jobs SET state = CASE WHEN cancel_requested THEN 'CANCELLED' WHEN attempts >= ? THEN 'FAILED' ELSE 'QUEUED' END, error = 'INTERRUPTED', updated_at = now() WHERE state = 'RUNNING'",
        MAX_DOWNLOAD_ATTEMPTS,
    )
  }

  override fun next(): DownloadJob? =
      jdbc
          .query(
              "SELECT * FROM download_jobs WHERE state = 'QUEUED' ORDER BY created_at LIMIT 1",
              ::decode,
          )
          .firstOrNull()

  override fun start(id: String): Boolean =
      jdbc.update(
          "UPDATE download_jobs SET state = 'RUNNING', attempts = attempts + 1, error = NULL, updated_at = now() WHERE id = ? AND state = 'QUEUED'",
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
        "UPDATE download_jobs SET state = CASE WHEN cancel_requested THEN 'CANCELLED' ELSE ? END, error = ?, updated_at = now() WHERE id = ?",
        state.name,
        error,
        id,
    )
  }

  override fun cancel(id: String): DownloadJob? {
    jdbc.update(
        "UPDATE download_jobs SET cancel_requested = true, state = CASE WHEN state = 'QUEUED' THEN 'CANCELLED' ELSE state END, updated_at = now() WHERE id = ? AND state IN ('QUEUED','RUNNING')",
        id,
    )
    return find(id)
  }

  override fun retry(id: String): DownloadJob? {
    jdbc.update(
        "UPDATE download_jobs SET state = 'QUEUED', attempts = 0, cancel_requested = false, error = NULL, updated_at = now() WHERE id = ? AND state IN ('FAILED','CANCELLED')",
        id,
    )
    return find(id)
  }

  private fun decode(row: ResultSet, @Suppress("UNUSED_PARAMETER") index: Int) =
      DownloadJob(
          row.getString("id"),
          mapper.readValue(row.getString("spec"), DownloadSpec::class.java),
          JobState.valueOf(row.getString("state")),
          row.getLong("bytes_done"),
          row.getInt("attempts"),
          row.getString("error"),
          row.getBoolean("cancel_requested"),
      )
}
