package com.sporesync.config.internal

import com.sporesync.config.InvalidSettingsException
import com.sporesync.config.SETTINGS_FILE_PROPERTY
import com.sporesync.config.SettingsViolation
import org.junit.jupiter.api.Assertions.assertEquals
import org.junit.jupiter.api.Test
import org.junit.jupiter.api.assertThrows
import java.nio.file.Path

private val NOT_A_FILE = listOf(SettingsViolation(SETTINGS_FILE_PROPERTY, "must name a regular file"))

class SettingsFileLocationTest {
    @Test
    fun `a blank location is rejected instead of resolving to the working directory`() {
        assertEquals(NOT_A_FILE, assertThrows<InvalidSettingsException> { settingsFilePath(null) }.violations)
        assertEquals(NOT_A_FILE, assertThrows<InvalidSettingsException> { settingsFilePath(Path.of("")) }.violations)
    }
}
