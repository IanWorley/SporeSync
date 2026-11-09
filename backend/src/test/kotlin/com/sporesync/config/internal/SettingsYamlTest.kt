package com.sporesync.config.internal

import com.sporesync.config.InvalidSettingsException
import com.sporesync.config.SettingsViolation
import org.junit.jupiter.api.Assertions.assertEquals
import org.junit.jupiter.api.Test
import org.junit.jupiter.api.assertThrows

private const val SOURCE_NAME = "settings.yaml"

class SettingsYamlTest {
    @Test
    fun `a file with more than one YAML document is rejected`() {
        val yaml =
            """
            general: { debug: true }
            ---
            connection: { port: 2222 }
            """.trimIndent()

        val error = assertThrows<InvalidSettingsException> { SettingsYaml.parse(yaml, SOURCE_NAME) }

        assertEquals(listOf(SettingsViolation(SOURCE_NAME, "must contain a single YAML document")), error.violations)
    }
}
