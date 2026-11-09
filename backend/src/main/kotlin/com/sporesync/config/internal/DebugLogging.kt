package com.sporesync.config.internal

import com.sporesync.config.ConfigChanged
import com.sporesync.config.GeneralSettings
import com.sporesync.config.SettingsStore
import org.springframework.boot.logging.LogLevel
import org.springframework.boot.logging.LoggingSystem
import org.springframework.context.event.EventListener
import org.springframework.stereotype.Component

private const val SPORESYNC_LOGGER = "com.sporesync"

@Component
internal class DebugLogging(
    private val loggingSystem: LoggingSystem,
    settings: SettingsStore,
) {
    private val startupLevel: LogLevel? = loggingSystem.getLoggerConfiguration(SPORESYNC_LOGGER)?.configuredLevel

    init {
        apply(settings.current.general)
    }

    @EventListener
    fun on(event: ConfigChanged) = apply(event.current.general)

    private fun apply(general: GeneralSettings) {
        loggingSystem.setLogLevel(SPORESYNC_LOGGER, if (general.debug) LogLevel.DEBUG else startupLevel)
    }
}
