package com.sporesync.config

import org.junit.jupiter.api.AfterAll
import org.junit.jupiter.api.Assertions.assertEquals
import org.junit.jupiter.api.Test
import org.junit.jupiter.api.assertThrows
import org.springframework.beans.factory.annotation.Autowired
import org.springframework.boot.logging.LogLevel
import org.springframework.boot.logging.LoggingSystem
import org.springframework.boot.test.context.TestConfiguration
import org.springframework.context.ApplicationEventPublisher
import org.springframework.context.annotation.Import
import org.springframework.context.event.EventListener
import org.springframework.core.annotation.Order
import org.springframework.modulith.test.ApplicationModuleTest
import org.springframework.modulith.test.AssertablePublishedEvents
import org.springframework.test.context.DynamicPropertyRegistry
import org.springframework.test.context.DynamicPropertySource
import java.nio.file.Files
import java.nio.file.Path

private const val TEMP_DIR_PREFIX = "sporesync-config-test"
private const val SETTINGS_FILE_NAME = "settings.yaml"
private const val SPORESYNC_LOGGER = "com.sporesync"
private const val FIRST = 1
private const val SECOND = 2

@ApplicationModuleTest
@Import(ConfigModuleTest.Listeners::class)
class ConfigModuleTest
    @Autowired
    constructor(
        private val store: SettingsStore,
        private val loggingSystem: LoggingSystem,
        private val listeners: Listeners,
        private val publisher: ApplicationEventPublisher,
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

        @Test
        fun `a failing ConfigChanged listener does not stop later listeners or fail the update`() {
            val current = store.update { it.copy(extraction = it.extraction.copy(enabled = false)) }

            assertEquals(current, listeners.received.lastOrNull()?.current)
        }

        @Test
        fun `a failing listener of any other event still fails its publisher`() {
            assertThrows<IllegalStateException> { publisher.publishEvent(UnrelatedEvent()) }
        }

        @TestConfiguration
        class Listeners {
            val received = mutableListOf<ConfigChanged>()

            @EventListener(ConfigChanged::class)
            @Order(FIRST)
            fun failFirst(): Unit = error("listener failed")

            @EventListener
            @Order(SECOND)
            fun recordSecond(event: ConfigChanged) {
                received += event
            }

            @EventListener(UnrelatedEvent::class)
            fun failUnrelated(): Unit = error("unrelated listener failed")
        }

        class UnrelatedEvent

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
