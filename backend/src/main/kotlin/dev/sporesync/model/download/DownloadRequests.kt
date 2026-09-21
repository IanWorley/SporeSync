package dev.sporesync.model.download

import dev.sporesync.config.SshConnectionSettings
import dev.sporesync.model.inventory.EntryType
import dev.sporesync.model.inventory.InventoryScanner
import dev.sporesync.model.settings.DownloadSettingsStore
import org.springframework.stereotype.Service

@Service
class DownloadRequests(
    private val jobs: DownloadJobRepository,
    private val inventory: InventoryScanner,
    private val configuration: DownloadSettingsStore,
) {
  fun enqueue(request: DownloadRequest): DownloadJob? {
    val settings = configuration.read()
    settings.validate()
    val connection =
        SshConnectionSettings(
            settings.host,
            settings.port,
            settings.username,
            settings.source,
            settings.timeoutMillis,
        )
    val entry =
        inventory.scan(connection).entries.singleOrNull {
          it.path == request.path && it.type == EntryType.file
        } ?: return null
    return jobs.enqueue(DownloadSpec(settings, entry))
  }
}
