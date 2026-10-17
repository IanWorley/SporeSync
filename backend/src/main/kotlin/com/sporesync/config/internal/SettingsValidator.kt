package com.sporesync.config.internal

import com.sporesync.config.AuthMethod
import com.sporesync.config.ConnectionSettings
import com.sporesync.config.InvalidSettingsException
import com.sporesync.config.Settings
import com.sporesync.config.SettingsViolation
import com.sporesync.config.internal.SettingsKeys.field

private const val MIN_PORT = 1
private const val MAX_PORT = 65_535
private const val MIN_LIMIT = 1

private const val PORT_RANGE_MESSAGE = "must be between $MIN_PORT and $MAX_PORT"
private const val MIN_LIMIT_MESSAGE = "must be at least $MIN_LIMIT"
private const val POSITIVE_MESSAGE = "must be greater than zero"
private const val REQUIRED_MESSAGE = "is required once connection.host is set"
internal const val BLANK_MESSAGE = "must not be blank"

/** Rules beyond what the types already enforce. Collects every violation so the user can fix them in one pass. */
internal object SettingsValidator {
    fun requireValid(settings: Settings): Settings {
        val violations = validate(settings)
        if (violations.isNotEmpty()) throw InvalidSettingsException(violations)
        return settings
    }

    fun validate(settings: Settings): List<SettingsViolation> =
        buildList {
            addAll(connectionViolations(settings.connection))

            if (settings.connection.isConfigured && settings.paths.remoteDir.isBlank()) {
                add(SettingsViolation(field(SettingsKeys.PATHS, SettingsKeys.REMOTE_DIR), REQUIRED_MESSAGE))
            }
            if (settings.paths.remoteScannerPath.isBlank()) {
                add(SettingsViolation(field(SettingsKeys.PATHS, SettingsKeys.REMOTE_SCANNER_PATH), BLANK_MESSAGE))
            }

            val transfer = settings.transfer
            if (transfer.concurrentFileLimit < MIN_LIMIT) {
                add(
                    SettingsViolation(
                        field(SettingsKeys.TRANSFER, SettingsKeys.CONCURRENT_FILE_LIMIT),
                        MIN_LIMIT_MESSAGE,
                    ),
                )
            }
            if (transfer.perFileConnectionLimit < MIN_LIMIT) {
                add(
                    SettingsViolation(
                        field(SettingsKeys.TRANSFER, SettingsKeys.PER_FILE_CONNECTION_LIMIT),
                        MIN_LIMIT_MESSAGE,
                    ),
                )
            }
            // `null` already means unlimited, so a set limit of zero would only stall every transfer.
            val bandwidthLimit = transfer.bandwidthLimit
            if (bandwidthLimit != null && bandwidthLimit.toBytes() <= 0) {
                add(SettingsViolation(field(SettingsKeys.TRANSFER, SettingsKeys.BANDWIDTH_LIMIT), POSITIVE_MESSAGE))
            }

            if (!settings.scan.remoteInterval.isPositive()) {
                add(SettingsViolation(field(SettingsKeys.SCAN, SettingsKeys.REMOTE_INTERVAL), POSITIVE_MESSAGE))
            }
            if (!settings.scan.localInterval.isPositive()) {
                add(SettingsViolation(field(SettingsKeys.SCAN, SettingsKeys.LOCAL_INTERVAL), POSITIVE_MESSAGE))
            }
        }

    private fun connectionViolations(connection: ConnectionSettings): List<SettingsViolation> =
        buildList {
            if (connection.port !in MIN_PORT..MAX_PORT) {
                add(SettingsViolation(field(SettingsKeys.CONNECTION, SettingsKeys.PORT), PORT_RANGE_MESSAGE))
            }
            // An unconfigured install is valid; credentials are only required once there is a host to use them on.
            if (!connection.isConfigured) return@buildList

            if (connection.username.isBlank()) {
                add(SettingsViolation(field(SettingsKeys.CONNECTION, SettingsKeys.USERNAME), REQUIRED_MESSAGE))
            }
            when (connection.authMethod) {
                AuthMethod.KEY -> {
                    if (connection.keyPath == null) {
                        add(SettingsViolation(field(SettingsKeys.CONNECTION, SettingsKeys.KEY_PATH), REQUIRED_MESSAGE))
                    }
                }

                AuthMethod.PASSWORD -> {
                    if (connection.password.isNullOrEmpty()) {
                        add(SettingsViolation(field(SettingsKeys.CONNECTION, SettingsKeys.PASSWORD), REQUIRED_MESSAGE))
                    }
                }
            }
        }
}
