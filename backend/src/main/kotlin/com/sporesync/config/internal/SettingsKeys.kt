package com.sporesync.config.internal

/**
 * Settings-file keys, shared by the YAML writer and validation messages so both name a field the same way.
 * Spring's relaxed binding maps each key onto the matching [com.sporesync.config.Settings] property, and an
 * environment variable such as `SPORESYNC_CONNECTION_PORT` onto `sporesync.connection.port`.
 */
internal object SettingsKeys {
    /** Environment and system properties under this prefix override values from the settings file. */
    const val PREFIX = "sporesync"

    const val GENERAL = "general"
    const val DEBUG = "debug"
    const val VERBOSE = "verbose"

    const val CONNECTION = "connection"
    const val HOST = "host"
    const val PORT = "port"
    const val USERNAME = "username"
    const val AUTH_METHOD = "auth-method"
    const val KEY_PATH = "key-path"
    const val PASSWORD = "password"

    const val PATHS = "paths"
    const val REMOTE_DIR = "remote-dir"
    const val LOCAL_DIR = "local-dir"
    const val REMOTE_SCANNER_PATH = "remote-scanner-path"

    const val TRANSFER = "transfer"
    const val CONCURRENT_FILE_LIMIT = "concurrent-file-limit"
    const val PER_FILE_CONNECTION_LIMIT = "per-file-connection-limit"
    const val BANDWIDTH_LIMIT = "bandwidth-limit"

    const val SCAN = "scan"
    const val REMOTE_INTERVAL = "remote-interval"
    const val LOCAL_INTERVAL = "local-interval"

    const val EXTRACTION = "extraction"
    const val ENABLED = "enabled"
    const val DELETE_ARCHIVE_AFTER_EXTRACT = "delete-archive-after-extract"

    const val AUTOQUEUE = "autoqueue"
    const val PATTERNS_ONLY = "patterns-only"

    fun field(
        group: String,
        key: String,
    ): String = "$group.$key"
}
