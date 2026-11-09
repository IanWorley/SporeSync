package com.sporesync.config

/** One rejected setting. [field] is the settings-file key, such as `connection.port`. */
data class SettingsViolation(
    val field: String,
    val message: String,
)

/** Settings failed to bind or validate. Messages name fields only, never values, so secrets stay out of logs. */
class InvalidSettingsException(
    val violations: List<SettingsViolation>,
) : RuntimeException("Invalid settings: " + violations.joinToString("; ") { "${it.field} ${it.message}" })
