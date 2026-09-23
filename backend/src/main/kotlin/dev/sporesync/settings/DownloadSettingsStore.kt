package dev.sporesync.settings

/** Reads and atomically saves validated, non-secret download settings. */
interface DownloadSettingsStore {
  fun read(): DownloadSettings

  fun save(value: DownloadSettings): DownloadSettings
}
