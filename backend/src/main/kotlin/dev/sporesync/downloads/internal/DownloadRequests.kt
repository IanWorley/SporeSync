package dev.sporesync.downloads.internal

import dev.sporesync.downloads.DownloadSpec
import dev.sporesync.inventory.EntryType
import dev.sporesync.inventory.InventoryScanner
import dev.sporesync.settings.DownloadSettingsStore
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
    val connection = settings.connection()
    val entry =
        inventory.scan(connection).entries.singleOrNull {
          it.path == request.path && it.type == EntryType.file
        } ?: return null
    return jobs.enqueue(DownloadSpec(settings, entry))
  }
}
