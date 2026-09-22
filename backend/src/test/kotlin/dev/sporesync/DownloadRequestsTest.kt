package dev.sporesync

import dev.sporesync.config.SshConnectionSettings
import dev.sporesync.model.download.DownloadJob
import dev.sporesync.model.download.DownloadJobRepository
import dev.sporesync.model.download.DownloadRequest
import dev.sporesync.model.download.DownloadRequests
import dev.sporesync.model.download.DownloadSpec
import dev.sporesync.model.download.JobState
import dev.sporesync.model.inventory.EntryType
import dev.sporesync.model.inventory.Inventory
import dev.sporesync.model.inventory.InventoryEntry
import dev.sporesync.model.inventory.InventoryScanner
import dev.sporesync.model.settings.DownloadSettings
import dev.sporesync.model.settings.DownloadSettingsStore
import org.junit.jupiter.api.Assertions.assertEquals
import org.junit.jupiter.api.Assertions.assertNull
import org.junit.jupiter.api.Test

class DownloadRequestsTest {
  private val settings =
      DownloadSettings(
          host = "seedbox.example",
          username = "scanner",
          source = "/remote",
          destination = "/local",
      )
  private val connection =
      SshConnectionSettings(
          settings.host,
          settings.port,
          settings.username,
          settings.source,
          settings.timeoutMillis,
      )
  private val jobs = RecordingJobs()
  private val configuration =
      object : DownloadSettingsStore {
        override fun read(): DownloadSettings = settings

        override fun save(value: DownloadSettings): DownloadSettings =
            error("Unexpected settings write")
      }

  private fun requests(entry: InventoryEntry): DownloadRequests {
    val scanner =
        object : InventoryScanner {
          override fun scan(connectionOverride: SshConnectionSettings?): Inventory {
            assertEquals(connection, connectionOverride)
            return Inventory(SCHEMA_VERSION, listOf(entry))
          }
        }
    return DownloadRequests(jobs, scanner, configuration)
  }

  @Test
  fun `discovered file is queued with the settings used to scan it`() {
    val entry = InventoryEntry(FILE_PATH, EntryType.file, FILE_BYTES, MODIFIED_TIME_NS)
    val expected = DownloadSpec(settings, entry)

    val result = requests(entry).enqueue(DownloadRequest(FILE_PATH))

    assertEquals(expected, result?.spec)
    assertEquals(listOf(expected), jobs.submitted)
  }

  @Test
  fun `a directory cannot be queued as a download`() {
    val entry = InventoryEntry(FILE_PATH, EntryType.directory, 0, MODIFIED_TIME_NS)

    assertNull(requests(entry).enqueue(DownloadRequest(FILE_PATH)))
    assertEquals(emptyList<DownloadSpec>(), jobs.submitted)
  }

  private class RecordingJobs : DownloadJobRepository {
    val submitted = mutableListOf<DownloadSpec>()

    override fun enqueue(spec: DownloadSpec): DownloadJob {
      submitted.add(spec)
      return DownloadJob("job", spec, JobState.QUEUED, 0, 0, null, false)
    }

    override fun list(): List<DownloadJob> = error("Unexpected queue read")

    override fun find(id: String): DownloadJob? = error("Unexpected queue read")

    override fun recover(): Unit = error("Unexpected recovery")

    override fun next(): DownloadJob? = error("Unexpected queue read")

    override fun start(id: String): Boolean = error("Unexpected start")

    override fun progress(id: String, bytes: Long): Unit = error("Unexpected progress")

    override fun finish(id: String, state: JobState, error: String?): Unit =
        error("Unexpected finish")

    override fun cancel(id: String): DownloadJob? = error("Unexpected cancellation")

    override fun retry(id: String): DownloadJob? = error("Unexpected retry")
  }

  private companion object {
    const val SCHEMA_VERSION = 1
    const val FILE_PATH = "nested/file.txt"
    const val FILE_BYTES = 128L
    const val MODIFIED_TIME_NS = 1_000_000_000L
  }
}
