package dev.sporesync.downloads

import dev.sporesync.ModuleTestDatabase
import dev.sporesync.downloads.internal.DownloadFailure
import dev.sporesync.downloads.internal.DownloadJobRepository
import dev.sporesync.downloads.internal.DownloadStorage
import dev.sporesync.downloads.internal.JobAction
import dev.sporesync.downloads.internal.JobState
import dev.sporesync.inventory.EntryType
import dev.sporesync.inventory.InventoryEntry
import dev.sporesync.inventory.InventoryScanner
import dev.sporesync.settings.DownloadSettings
import dev.sporesync.settings.DownloadSettingsStore
import dev.sporesync.ssh.SshSettings
import java.nio.file.Files
import java.nio.file.Path
import org.junit.jupiter.api.Assertions.assertEquals
import org.junit.jupiter.api.Assertions.assertThrows
import org.junit.jupiter.api.BeforeEach
import org.junit.jupiter.api.Test
import org.junit.jupiter.api.io.TempDir
import org.mockito.Mockito.doThrow
import org.springframework.beans.factory.annotation.Autowired
import org.springframework.context.annotation.Import
import org.springframework.jdbc.core.JdbcTemplate
import org.springframework.modulith.test.ApplicationModuleTest
import org.springframework.test.context.TestPropertySource
import org.springframework.test.context.bean.override.mockito.MockitoBean
import org.springframework.test.context.bean.override.mockito.MockitoSpyBean
import tools.jackson.databind.json.JsonMapper

@ApplicationModuleTest
@Import(ModuleTestDatabase::class)
@TestPropertySource(properties = ["sporesync.background.enabled=false"])
class DownloadsModuleTest {
  @MockitoSpyBean private lateinit var storage: DownloadStorage
  @Autowired private lateinit var queue: DownloadQueue
  @Autowired private lateinit var jobs: DownloadJobRepository
  @Autowired private lateinit var jdbc: JdbcTemplate
  @Autowired private lateinit var mapper: JsonMapper
  @MockitoBean private lateinit var scanner: InventoryScanner
  @MockitoBean private lateinit var settings: DownloadSettingsStore
  @MockitoBean private lateinit var credentials: SshSettings

  @BeforeEach
  fun clearJobs() {
    jdbc.update("DELETE FROM download_jobs")
  }

  @Test
  fun `repeated stable observations create one durable queued job`(@TempDir destination: Path) {
    val spec = specification(destination)

    queue.acceptStable(spec)
    queue.acceptStable(spec)

    val job = jobs.list().single()
    assertEquals(spec, job.spec)
    assertEquals(JobState.QUEUED, job.state)
  }

  @Test
  fun `a missing completed file is requeued with cleared progress`(@TempDir destination: Path) {
    val spec = specification(destination)
    queue.acceptStable(spec)
    jobs.progress(spec.identity(), FILE_BYTES)
    jobs.finish(spec.identity(), JobState.COMPLETE)

    queue.acceptStable(spec)

    val job = requireNotNull(jobs.find(spec.identity()))
    assertEquals(JobState.QUEUED, job.state)
    assertEquals(0L, job.bytesDone)
    assertEquals(0, job.attempts)
  }

  @Test
  fun `automatic reconciliation preserves an explicit pause`(@TempDir destination: Path) {
    val spec = specification(destination)
    queue.acceptStable(spec)
    val paused = requireNotNull(jobs.requestAction(spec.identity(), JobAction.PAUSE))
    jobs.completeAction(paused)

    queue.acceptStable(spec)

    assertEquals(JobState.PAUSED, jobs.find(spec.identity())?.state)
  }

  @Test
  fun `an unsafe destination is not treated as a missing completed file`(@TempDir root: Path) {
    val destination = Files.createDirectory(root.resolve("downloads"))
    val outside = Files.createDirectory(root.resolve("outside"))
    Files.createSymbolicLink(destination.resolve("nested"), outside)
    val spec = specification(destination)
    queue.acceptStable(spec)
    jobs.finish(spec.identity(), JobState.COMPLETE)

    queue.acceptStable(spec)

    assertEquals(JobState.COMPLETE, jobs.find(spec.identity())?.state)
  }

  @Test
  fun `storage inspection failure reaches the queue caller`(@TempDir destination: Path) {
    val spec = specification(destination)
    queue.acceptStable(spec)
    jobs.finish(spec.identity(), JobState.COMPLETE)
    doThrow(DownloadFailure("LOCAL_IO_FAILED"))
        .`when`(storage)
        .missing(spec.settings.destination, spec.entry.path)

    val error = assertThrows(DownloadFailure::class.java) { queue.acceptStable(spec) }

    assertEquals("LOCAL_IO_FAILED", error.message)
    assertEquals(JobState.COMPLETE, jobs.find(spec.identity())?.state)
  }

  @Test
  fun `pre-modulith job snapshots keep their stored shape and identity`() {
    val json = requireNotNull(javaClass.getResource("/legacy-download-spec.json")).readText()
    jdbc.update(
        "INSERT INTO download_jobs (id, spec, state) VALUES (?, ?, ?)",
        LEGACY_JOB_ID,
        json,
        "PAUSED",
    )

    val job = requireNotNull(jobs.find(LEGACY_JOB_ID))

    assertEquals(specification(Path.of("/local")), job.spec)
    assertEquals(LEGACY_JOB_ID, job.spec.identity())
    assertEquals(JobState.PAUSED, job.state)
    assertEquals(mapper.readTree(json), mapper.readTree(mapper.writeValueAsString(job.spec)))
  }

  private fun specification(destination: Path) =
      DownloadSpec(
          DownloadSettings(
              host = "seedbox.example",
              username = "scanner",
              source = "/remote",
              destination = destination.toString(),
          ),
          InventoryEntry("nested/file.txt", EntryType.file, FILE_BYTES, MODIFIED_TIME_NS),
      )

  private companion object {
    const val FILE_BYTES = 128L
    const val MODIFIED_TIME_NS = 1_000_000_000L
    const val LEGACY_JOB_ID = "7e8b1919f8851000704f7cd54ef1925921348a200dafa080a793eba64bac536f"
  }
}
