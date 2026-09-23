package dev.sporesync.settings.internal

import dev.sporesync.settings.DEFAULT_SCAN_SECONDS
import dev.sporesync.settings.DEFAULT_SSH_PORT
import dev.sporesync.settings.DEFAULT_TIMEOUT_MILLIS
import dev.sporesync.settings.DownloadSettings
import dev.sporesync.settings.DownloadSettingsStore
import org.springframework.stereotype.Service

@Service
class DownloadConfiguration(private val repository: ApplicationSettingRepository) :
    DownloadSettingsStore {
  override fun read(): DownloadSettings {
    val values = repository.findAll().associate { it.name to it.value }
    return DownloadSettings(
        host = values[SettingNames.SSH_HOST].orEmpty(),
        timeoutMillis = values[SettingNames.SSH_TIMEOUT_MILLIS]?.toInt() ?: DEFAULT_TIMEOUT_MILLIS,
        port = values[SettingNames.SSH_PORT]?.toInt() ?: DEFAULT_SSH_PORT,
        username = values[SettingNames.SSH_USERNAME].orEmpty(),
        source = values[SettingNames.REMOTE_SOURCE_DIRECTORY].orEmpty(),
        destination = values[SettingNames.LOCAL_DOWNLOAD_DIRECTORY].orEmpty(),
        scanSeconds = values[SettingNames.SCAN_INTERVAL]?.toLong() ?: DEFAULT_SCAN_SECONDS,
        automatic = values[SettingNames.AUTOMATIC_DOWNLOAD_ENABLED]?.toBooleanStrict() ?: true,
        temporaryFiles = values[SettingNames.TEMPORARY_FILE_ENABLED]?.toBooleanStrict() ?: true,
    )
  }

  override fun save(value: DownloadSettings): DownloadSettings {
    value.validate()
    // saveAll supplies one transaction: the worker never reads a partially saved form.
    repository.saveAll(
        mapOf(
                SettingNames.SSH_HOST to value.host,
                SettingNames.SSH_TIMEOUT_MILLIS to value.timeoutMillis.toString(),
                SettingNames.SSH_PORT to value.port.toString(),
                SettingNames.SSH_USERNAME to value.username,
                SettingNames.REMOTE_SOURCE_DIRECTORY to value.source,
                SettingNames.LOCAL_DOWNLOAD_DIRECTORY to value.destination,
                SettingNames.SCAN_INTERVAL to value.scanSeconds.toString(),
                SettingNames.AUTOMATIC_DOWNLOAD_ENABLED to value.automatic.toString(),
                SettingNames.TEMPORARY_FILE_ENABLED to value.temporaryFiles.toString(),
            )
            .map { (name, content) -> ApplicationSetting(name, content) }
    )
    return value
  }
}
