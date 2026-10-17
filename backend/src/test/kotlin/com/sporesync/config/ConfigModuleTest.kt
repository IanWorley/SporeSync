package com.sporesync.config

import org.junit.jupiter.api.AfterAll
import org.junit.jupiter.api.Assertions.assertEquals
import org.junit.jupiter.api.Test
import org.springframework.beans.factory.annotation.Autowired
import org.springframework.boot.logging.LogLevel
import org.springframework.boot.logging.LoggingSystem
import org.springframework.modulith.test.ApplicationModuleTest
import org.springframework.modulith.test.AssertablePublishedEvents
import org.springframework.test.context.DynamicPropertyRegistry
import org.springframework.test.context.DynamicPropertySource
import java.nio.file.Files
import java.nio.file.Path

private const val TEMP_DIR_PREFIX = "sporesync-config-test"
private const val SETTINGS_FILE_NAME = "settings.yaml"
private const val SPORESYNC_LOGGER = "com.sporesync"

/** Boots the config module on its own, the way other modules will see it: an injected [SettingsStore]. */
@ApplicationModuleTest
class ConfigModuleTest
    @Autowired
    constructor(
        private val store: SettingsStore,
        private val loggingSystem: LoggingSystem,
    ) {
        @Test
        fun `an update publishes ConfigChanged with the old and new settings`(events: AssertablePublishedEvents) {
            val previous = store.current

            val current = store.update { it.copy(autoqueue = it.autoqueue.copy(patternsOnly = true)) }

            assertEquals(listOf(ConfigChanged(previous, current)), events.ofType(ConfigChanged::class.java).toList())
        }

        @Test
        fun `the debug setting switches SporeSync logging to DEBUG and back`() {
            store.update { it.copy(general = it.general.copy(debug = true)) }
            val debugOn = loggingSystem.getLoggerConfiguration(SPORESYNC_LOGGER)?.configuredLevel

            store.update { it.copy(general = it.general.copy(debug = false)) }
            val debugOff = loggingSystem.getLoggerConfiguration(SPORESYNC_LOGGER)?.configuredLevel

            assertEquals(LogLevel.DEBUG, debugOn)
            assertEquals(LogLevel.INFO, debugOff)
        }

        companion object {
            private val directory: Path = Files.createTempDirectory(TEMP_DIR_PREFIX)

            @JvmStatic
            @DynamicPropertySource
            fun settingsFile(registry: DynamicPropertyRegistry) {
                registry.add(SETTINGS_FILE_PROPERTY) { directory.resolve(SETTINGS_FILE_NAME).toString() }
            }

            @JvmStatic
            @AfterAll
            fun deleteDirectory() {
                directory.toFile().deleteRecursively()
            }
        }
    }
