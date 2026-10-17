package com.sporesync.config.internal

import com.sporesync.config.AuthMethod
import com.sporesync.config.InvalidSettingsException
import com.sporesync.config.Settings
import com.sporesync.config.SettingsViolation
import org.junit.jupiter.api.Assertions.assertEquals
import org.junit.jupiter.api.Test
import org.junit.jupiter.api.assertThrows
import org.springframework.boot.context.properties.source.ConfigurationPropertySource
import org.springframework.core.env.SystemEnvironmentPropertySource
import org.springframework.util.unit.DataSize
import java.time.Duration

private const val SOURCE_NAME = "settings.yaml"

// Spring only applies relaxed environment-variable mapping to sources with this name suffix.
private const val ENVIRONMENT_SOURCE_NAME = "test-systemEnvironment"

class SettingsBinderTest {
    @Test
    fun `binds every group from the settings file`() {
        val yaml =
            """
            general: { debug: true, verbose: true }
            connection: { host: seedbox.example, port: 2222, username: ian, auth-method: KEY, key-path: /keys/id }
            paths: { remote-dir: /home/ian/done, local-dir: /data, remote-scanner-path: /tmp/scanner }
            transfer: { concurrent-file-limit: 3, per-file-connection-limit: 5, bandwidth-limit: 10MB }
            scan: { remote-interval: 1m, local-interval: 5s }
            extraction: { enabled: false, delete-archive-after-extract: true }
            autoqueue: { enabled: false, patterns-only: true }
            """.trimIndent()

        val settings = SettingsBinder.bind(SettingsYaml.parse(yaml, SOURCE_NAME))

        assertEquals(true, settings.general.verbose)
        assertEquals(AuthMethod.KEY, settings.connection.authMethod)
        assertEquals("/home/ian/done", settings.paths.remoteDir)
        assertEquals(DataSize.ofMegabytes(10), settings.transfer.bandwidthLimit)
        assertEquals(Duration.ofMinutes(1), settings.scan.remoteInterval)
        assertEquals(true, settings.extraction.deleteArchiveAfterExtract)
        assertEquals(true, settings.autoqueue.patternsOnly)
    }

    @Test
    fun `environment variables override the settings file`() {
        val file = SettingsYaml.parse("connection: { port: 2222 }", SOURCE_NAME)
        val environment = environmentSource(mapOf("SPORESYNC_CONNECTION_PORT" to "2200"))

        val settings = SettingsBinder.bind(file, listOfNotNull(environment))

        assertEquals(2200, settings.connection.port)
    }

    @Test
    fun `missing values fall back to defaults`() {
        assertEquals(Settings(), SettingsBinder.bind(null))
    }

    @Test
    fun `a value of the wrong type is rejected with its field name`() {
        val file = SettingsYaml.parse("transfer: { concurrent-file-limit: lots }", SOURCE_NAME)

        val error = assertThrows<InvalidSettingsException> { SettingsBinder.bind(file) }

        assertEquals(
            listOf(SettingsViolation("transfer.concurrent-file-limit", "is not a valid int")),
            error.violations,
        )
    }

    @Test
    fun `an unknown key in the settings file is rejected with its field name`() {
        val file = SettingsYaml.parse("connection: { prot: 2222 }", SOURCE_NAME)

        val error = assertThrows<InvalidSettingsException> { SettingsBinder.bind(file) }

        assertEquals(listOf(SettingsViolation("connection.prot", "is not a recognized setting")), error.violations)
    }

    @Test
    fun `rendered settings bind back to the same values`() {
        val settings = SettingsBinder.bind(SettingsYaml.parse(sampleYaml(), SOURCE_NAME))

        val roundTripped =
            SettingsBinder.bind(SettingsYaml.parse(SettingsYaml.render(settings), SOURCE_NAME))

        assertEquals(settings, roundTripped)
    }

    private fun sampleYaml() =
        """
        connection: { host: seedbox.example, username: ian, password: "p@ss: word" }
        transfer: { bandwidth-limit: 512KB }
        scan: { local-interval: 1500ms }
        """.trimIndent()

    private fun environmentSource(variables: Map<String, Any>): ConfigurationPropertySource? =
        ConfigurationPropertySource.from(SystemEnvironmentPropertySource(ENVIRONMENT_SOURCE_NAME, variables))
}
