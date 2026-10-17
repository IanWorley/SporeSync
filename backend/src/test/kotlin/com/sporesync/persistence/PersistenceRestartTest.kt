package com.sporesync.persistence

import com.sporesync.SporeSyncApplication
import org.junit.jupiter.api.Assertions.assertEquals
import org.junit.jupiter.api.Assertions.assertTrue
import org.junit.jupiter.api.Test
import org.junit.jupiter.api.io.TempDir
import org.springframework.boot.WebApplicationType
import org.springframework.boot.builder.SpringApplicationBuilder
import org.springframework.context.ConfigurableApplicationContext
import org.springframework.jdbc.core.JdbcTemplate
import org.springframework.jdbc.core.queryForList
import java.nio.file.Files
import java.nio.file.Path
import java.time.Instant

private const val DIRECTORY_PROPERTY = "sporesync.persistence.directory"
private const val DATABASE_FILE = "sporesync.mv.db"
private val FINISHED_AT: Instant = Instant.parse("2026-01-02T03:04:05Z")

/** Starts the real application against an H2 file in a temporary directory, so each test sees a fresh database. */
class PersistenceRestartTest {
    @TempDir
    lateinit var dataDir: Path

    @Test
    fun `liquibase creates the schema in an H2 file under the configured directory`() {
        startApp().use { context ->
            val jdbc = context.getBean(JdbcTemplate::class.java)

            val tables =
                jdbc.queryForList<String>(
                    "SELECT TABLE_NAME FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_SCHEMA = 'PUBLIC'",
                )
            val changeSets = jdbc.queryForList<String>("SELECT ID FROM DATABASECHANGELOG ORDER BY ORDEREXECUTED")

            assertTrue(
                tables.containsAll(listOf("AUTOQUEUE_PATTERN", "TRANSFER_HISTORY", "SETTINGS_CACHE")),
                "tables: $tables",
            )
            assertEquals(
                listOf("0001-autoqueue-pattern", "0001-transfer-history", "0001-settings-cache"),
                changeSets,
            )
        }
        assertTrue(Files.exists(dataDir.resolve(DATABASE_FILE)))
    }

    @Test
    fun `records saved before a restart are read back afterwards`() {
        val connectionHost = SettingKey("connection", "host")
        startApp().use { context ->
            context.getBean(AutoQueuePatternRepository::class.java).save(AutoQueuePattern("*.mkv"))
            context.getBean(TransferHistoryRepository::class.java).saveAll(
                listOf(
                    TransferHistoryEntry.completed("shows/episode.mkv", FINISHED_AT),
                    TransferHistoryEntry.failed("movies/film.mkv", FINISHED_AT, "connection reset"),
                ),
            )
            context.getBean(SettingsCacheRepository::class.java).save(SettingsCacheEntry(connectionHost, "seedbox"))
        }

        startApp().use { context ->
            val patterns = context.getBean(AutoQueuePatternRepository::class.java).findAll()
            val history = context.getBean(TransferHistoryRepository::class.java).findAll().sortedBy { it.id }
            val host = context.getBean(SettingsCacheRepository::class.java).findById(connectionHost).orElseThrow()

            assertEquals(listOf("*.mkv"), patterns.map { it.pattern })
            assertEquals(
                listOf(
                    HistoryRow("shows/episode.mkv", TransferOutcome.COMPLETED, FINISHED_AT, null),
                    HistoryRow("movies/film.mkv", TransferOutcome.FAILED, FINISHED_AT, "connection reset"),
                ),
                history.map { HistoryRow(it.path, it.outcome, it.finishedAt, it.failureReason) },
            )
            assertEquals("seedbox", host.value)
        }
    }

    // Command-line arguments outrank the test application.yaml, which would otherwise pick its own temp directory.
    private fun startApp(): ConfigurableApplicationContext =
        SpringApplicationBuilder(SporeSyncApplication::class.java)
            .web(WebApplicationType.NONE)
            .run("--$DIRECTORY_PROPERTY=$dataDir")

    private data class HistoryRow(
        val path: String,
        val outcome: TransferOutcome,
        val finishedAt: Instant,
        val failureReason: String?,
    )
}
