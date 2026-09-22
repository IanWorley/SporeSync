package dev.sporesync.model.settings

import java.nio.file.Path
import org.springframework.stereotype.Service
import org.springframework.web.bind.annotation.*

const val DEFAULT_SCAN_SECONDS = 300L
const val MIN_SCAN_SECONDS = 10L
const val MAX_SCAN_SECONDS = 86400L
const val MIN_SSH_PORT = 1
const val MAX_SSH_PORT = 65535
const val DEFAULT_SSH_PORT = 22
const val DEFAULT_TIMEOUT_MILLIS = 30000
const val MAX_TIMEOUT_MILLIS = 300000

/** Credentials and trust material deliberately have no representation in the browser contract. */
data class DownloadSettings(
    val host: String = "",
    val port: Int = DEFAULT_SSH_PORT,
    val username: String = "",
    val source: String = "",
    val destination: String = "",
    val scanSeconds: Long = DEFAULT_SCAN_SECONDS,
    val automatic: Boolean = true,
    val temporaryFiles: Boolean = true,
    val timeoutMillis: Int = DEFAULT_TIMEOUT_MILLIS,
) {
  fun validate() {
    validateDiscovery()
    require(destination.isNotBlank() && Path.of(destination).isAbsolute)
  }

  fun validateDiscovery() {
    require(host.isNotBlank() && username.isNotBlank())
    require(port in MIN_SSH_PORT..MAX_SSH_PORT)
    require(timeoutMillis in 1..MAX_TIMEOUT_MILLIS)
    require(source.startsWith('/') && '\u0000' !in source)
    require(scanSeconds in MIN_SCAN_SECONDS..MAX_SCAN_SECONDS)
  }
}

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
