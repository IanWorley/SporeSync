package com.sporesync.config

import com.sporesync.config.internal.SettingsBinder
import com.sporesync.config.internal.SettingsValidator
import com.sporesync.config.internal.SettingsYaml
import com.sporesync.config.internal.settingsFilePath
import org.slf4j.LoggerFactory
import org.springframework.beans.factory.annotation.Value
import org.springframework.boot.context.properties.source.ConfigurationPropertySource
import org.springframework.boot.context.properties.source.ConfigurationPropertySources
import org.springframework.context.ApplicationEventPublisher
import org.springframework.core.env.ConfigurableEnvironment
import org.springframework.stereotype.Service
import java.io.IOException
import java.nio.file.Files
import java.nio.file.Path
import java.nio.file.StandardCopyOption
import java.nio.file.attribute.PosixFilePermissions

const val SETTINGS_FILE_PROPERTY = "sporesync.settings-file"
private const val DEFAULT_SETTINGS_FILE = "/config/settings.yaml"
private const val TEMP_FILE_SUFFIX = ".tmp"
private const val POSIX_VIEW = "posix"
private const val OWNER_ONLY_PERMISSIONS = "rw-------"

/**
 * The only reader and writer of the settings file. Loads and validates it on startup, so an invalid file stops
 * the application with field-level messages. A missing file means defaults; it is created on the first update.
 *
 * Environment variables override file values through Spring's relaxed binding (for example
 * `SPORESYNC_CONNECTION_PORT=2222`). Overrides affect [current] but are never written to the file.
 */
@Service
class SettingsStore(
    private val environment: ConfigurableEnvironment,
    private val events: ApplicationEventPublisher,
    @Value("\${$SETTINGS_FILE_PROPERTY:$DEFAULT_SETTINGS_FILE}") settingsFileLocation: Path?,
) {
    private val settingsFile: Path = settingsFilePath(settingsFileLocation)
    private val log = LoggerFactory.getLogger(SettingsStore::class.java)

    private var fileSettings: Settings

    @Volatile
    private var effectiveSettings: Settings

    init {
        val fileSource = readFile()
        fileSettings = SettingsBinder.bind(fileSource)
        effectiveSettings = effective(fileSource)
        log.info("Loaded settings from {}", settingsFile)
    }

    /** Effective settings: the file plus environment overrides, validated. */
    val current: Settings get() = effectiveSettings

    /**
     * Applies [change] to the file's settings, validates the result with environment overrides applied, writes
     * the file, and publishes [ConfigChanged]. Throws [InvalidSettingsException] and leaves the file untouched when
     * the result is invalid.
     */
    @Synchronized
    fun update(change: (Settings) -> Settings): Settings {
        val next = change(fileSettings)
        val content = SettingsYaml.render(next)
        val updated = effective(SettingsYaml.parse(content, settingsFile.fileName.toString()))

        writeFile(content)
        val previous = effectiveSettings
        fileSettings = next
        effectiveSettings = updated
        log.info("Saved settings to {}", settingsFile)

        notify(ConfigChanged(previous, updated))
        return updated
    }

    // The update is already committed; a failing listener is that listener's bug, not a failed save.
    @Suppress("TooGenericExceptionCaught")
    private fun notify(event: ConfigChanged) {
        try {
            events.publishEvent(event)
        } catch (e: RuntimeException) {
            log.error("A ConfigChanged listener failed after settings were saved", e)
        }
    }

    private fun effective(fileSource: ConfigurationPropertySource?): Settings {
        val overrides = ConfigurationPropertySources.from(environment.propertySources)
        return SettingsValidator.requireValid(SettingsBinder.bind(fileSource, overrides.toList()))
    }

    private fun readFile(): ConfigurationPropertySource? {
        if (!Files.exists(settingsFile)) {
            log.info("No settings file at {}; using defaults", settingsFile)
            return null
        }
        restrictToOwner(settingsFile)
        return SettingsYaml.parse(Files.readString(settingsFile), settingsFile.fileName.toString())
    }

    private fun restrictToOwner(path: Path) {
        if (!supportsPosixPermissions(path)) return
        val ownerOnly = PosixFilePermissions.fromString(OWNER_ONLY_PERMISSIONS)
        if (Files.getPosixFilePermissions(path) == ownerOnly) return
        try {
            Files.setPosixFilePermissions(path, ownerOnly)
            log.warn("Restricted {} to owner-only permissions because it can hold secrets", path)
        } catch (e: IOException) {
            log.warn("Could not restrict {} to owner-only permissions: {}", path, e.javaClass.simpleName)
        }
    }

    private fun writeFile(content: String) {
        val directory = settingsFile.toAbsolutePath().parent
        Files.createDirectories(directory)
        val temp = Files.createTempFile(directory, settingsFile.fileName.toString(), TEMP_FILE_SUFFIX)
        try {
            Files.writeString(temp, content)
            restrictToOwner(temp)
            Files.move(temp, settingsFile, StandardCopyOption.REPLACE_EXISTING, StandardCopyOption.ATOMIC_MOVE)
        } finally {
            Files.deleteIfExists(temp)
        }
    }

    private fun supportsPosixPermissions(path: Path): Boolean =
        path.fileSystem.supportedFileAttributeViews().contains(POSIX_VIEW)
}
