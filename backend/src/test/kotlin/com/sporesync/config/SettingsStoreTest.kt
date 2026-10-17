package com.sporesync.config

import org.junit.jupiter.api.Assertions.assertEquals
import org.junit.jupiter.api.Assertions.assertFalse
import org.junit.jupiter.api.Assumptions.assumeTrue
import org.junit.jupiter.api.Test
import org.junit.jupiter.api.assertThrows
import org.junit.jupiter.api.io.TempDir
import org.springframework.context.ApplicationEventPublisher
import org.springframework.core.env.SystemEnvironmentPropertySource
import org.springframework.mock.env.MockEnvironment
import java.nio.file.Files
import java.nio.file.Path
import java.nio.file.attribute.PosixFilePermissions
import java.time.Duration

private const val SETTINGS_FILE_NAME = "settings.yaml"

// Spring only applies relaxed environment-variable mapping to sources with this name suffix.
private const val ENVIRONMENT_SOURCE_NAME = "test-systemEnvironment"
private const val SECRET_FROM_ENVIRONMENT = "from-env-secret"
private const val POSIX_VIEW = "posix"
private const val WORLD_READABLE = "rw-r--r--"
private const val OWNER_ONLY = "rw-------"

class SettingsStoreTest {
    @TempDir
    private lateinit var directory: Path

    private val settingsFile: Path get() = directory.resolve(SETTINGS_FILE_NAME)

    private val ignoreEvents = ApplicationEventPublisher { }

    @Test
    fun `loads the settings file on startup`() {
        Files.writeString(settingsFile, "transfer: { concurrent-file-limit: 7 }")

        assertEquals(7, store().current.transfer.concurrentFileLimit)
    }

    @Test
    fun `an existing settings file readable by others is restricted to its owner on startup`() {
        Files.writeString(settingsFile, "connection: { password: secret }")
        assumeTrue(settingsFile.fileSystem.supportedFileAttributeViews().contains(POSIX_VIEW))
        Files.setPosixFilePermissions(settingsFile, PosixFilePermissions.fromString(WORLD_READABLE))

        store()

        assertEquals(PosixFilePermissions.fromString(OWNER_ONLY), Files.getPosixFilePermissions(settingsFile))
    }

    @Test
    fun `updates are written to the file and survive a restart`() {
        store().update { it.copy(scan = it.scan.copy(localInterval = Duration.ofSeconds(3))) }

        assertEquals(Duration.ofSeconds(3), store().current.scan.localInterval)
    }

    @Test
    fun `an invalid update is rejected and leaves the file untouched`() {
        Files.writeString(settingsFile, "transfer: { concurrent-file-limit: 7 }")
        val store = store()

        assertThrows<InvalidSettingsException> {
            store.update { it.copy(transfer = it.transfer.copy(concurrentFileLimit = 0)) }
        }

        assertEquals("transfer: { concurrent-file-limit: 7 }", Files.readString(settingsFile))
        assertEquals(7, store.current.transfer.concurrentFileLimit)
    }

    @Test
    fun `environment overrides apply but are never written to the file`() {
        val environment =
            MockEnvironment().apply {
                propertySources.addFirst(
                    SystemEnvironmentPropertySource(
                        ENVIRONMENT_SOURCE_NAME,
                        mapOf("SPORESYNC_CONNECTION_PASSWORD" to SECRET_FROM_ENVIRONMENT),
                    ),
                )
            }
        val store = SettingsStore(environment, ignoreEvents, settingsFile)

        store.update { it.copy(general = it.general.copy(verbose = true)) }

        assertEquals(SECRET_FROM_ENVIRONMENT, store.current.connection.password)
        assertFalse(Files.readString(settingsFile).contains(SECRET_FROM_ENVIRONMENT))
    }

    private fun store() = SettingsStore(MockEnvironment(), ignoreEvents, settingsFile)
}
