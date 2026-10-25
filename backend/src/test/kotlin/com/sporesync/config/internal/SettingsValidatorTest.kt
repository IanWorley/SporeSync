package com.sporesync.config.internal

import com.sporesync.config.AuthMethod
import com.sporesync.config.ConnectionSettings
import com.sporesync.config.PathSettings
import com.sporesync.config.ScanSettings
import com.sporesync.config.Settings
import com.sporesync.config.SettingsViolation
import com.sporesync.config.TransferSettings
import org.junit.jupiter.api.Assertions.assertEquals
import org.junit.jupiter.api.Test
import org.springframework.util.unit.DataSize
import java.time.Duration

class SettingsValidatorTest {
    @Test
    fun `defaults are valid`() {
        assertEquals(emptyList<SettingsViolation>(), SettingsValidator.validate(Settings()))
    }

    @Test
    fun `out-of-range values are reported per field`() {
        val settings =
            Settings(
                connection = ConnectionSettings(port = 0),
                transfer =
                    TransferSettings(
                        concurrentFileLimit = 0,
                        perFileConnectionLimit = -1,
                        bandwidthLimit = DataSize.ofBytes(0),
                    ),
                scan = ScanSettings(remoteInterval = Duration.ZERO),
            )

        assertEquals(
            listOf(
                SettingsViolation("connection.port", "must be between 1 and 65535"),
                SettingsViolation("transfer.concurrent-file-limit", "must be at least 1"),
                SettingsViolation("transfer.per-file-connection-limit", "must be at least 1"),
                SettingsViolation("transfer.bandwidth-limit", "must be greater than zero"),
                SettingsViolation("scan.remote-interval", "must be greater than zero"),
            ),
            SettingsValidator.validate(settings),
        )
    }

    @Test
    fun `a configured host requires the credentials for its auth method`() {
        val settings =
            Settings(
                connection = ConnectionSettings(host = "seedbox.example", authMethod = AuthMethod.KEY),
                paths = PathSettings(remoteDir = "/done"),
            )

        assertEquals(
            listOf(
                SettingsViolation("connection.username", "is required once connection.host is set"),
                SettingsViolation("connection.key-path", "is required once connection.host is set"),
            ),
            SettingsValidator.validate(settings),
        )
    }
}
