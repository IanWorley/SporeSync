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

/** Property naming the settings file, so tests and local runs can point away from the Docker volume. */
const val SETTINGS_FILE_PROPERTY = "sporesync.settings-file"
private const val DEFAULT_SETTINGS_FILE = "/config/settings.yaml"
private const val TEMP_FILE_SUFFIX = ".tmp"
private const val POSIX_VIEW = "posix"

// The file can hold the SSH password, so only the owner may read it.
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

    /** What the file says, without environment overrides. Updates start from this so overrides stay out of it. */
    private var persisted: Settings

    @Volatile
    private var effectiveSettings: Settings

    init {
        val fileSource = readFile()
        persisted = SettingsBinder.bind(fileSource)
        effectiveSettings = effective(fileSource)
        log.info("Loaded settings from {}", settingsFile)
    }

    /** Effective settings: the file plus environment overrides, validated. */
    val current: Settings get() = effectiveSettings

    /**
     * Applies [change] to the persisted settings, validates the result with environment overrides applied, writes
     * the file, and publishes [ConfigChanged]. Throws [InvalidSettingsException] and leaves the file untouched when
     * the result is invalid.
     */
    @Synchronized
    fun update(change: (Settings) -> Settings): Settings {
        val next = change(persisted)
        val content = SettingsYaml.render(next)
        val updated = effective(SettingsYaml.parse(content, settingsFile.fileName.toString()))

        writeFile(content)
        val previous = effectiveSettings
        persisted = next
        effectiveSettings = updated
        log.info("Saved settings to {}", settingsFile)

        events.publishEvent(ConfigChanged(previous, updated))
        return updated
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

    // A file created by hand or by deployment tooling may be world-readable; tighten it before trusting its secrets.
    // A read-only mount cannot be changed, so that case is logged rather than stopping startup.
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

    // Written to a sibling file and moved into place, so a crash mid-write never leaves a truncated settings file.
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
