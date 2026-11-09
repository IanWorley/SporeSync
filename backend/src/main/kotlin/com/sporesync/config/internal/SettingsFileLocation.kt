package com.sporesync.config.internal

import com.sporesync.config.InvalidSettingsException
import com.sporesync.config.SETTINGS_FILE_PROPERTY
import com.sporesync.config.SettingsViolation
import java.nio.file.Files
import java.nio.file.Path

private const val NOT_A_FILE_MESSAGE = "must name a regular file"

// Spring converts a blank property to `null`, and an empty path resolves to the working directory.
internal fun settingsFilePath(location: Path?): Path =
    location?.takeIf { it.toString().isNotBlank() && (!Files.exists(it) || Files.isRegularFile(it)) }
        ?: throw InvalidSettingsException(listOf(SettingsViolation(SETTINGS_FILE_PROPERTY, NOT_A_FILE_MESSAGE)))
