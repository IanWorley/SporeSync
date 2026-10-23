package com.sporesync.config

/** Published after every successful [SettingsStore.update], once the new settings are on disk. */
data class ConfigChanged(
    val previous: Settings,
    val current: Settings,
)
