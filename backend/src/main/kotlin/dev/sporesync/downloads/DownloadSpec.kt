package dev.sporesync.downloads

import dev.sporesync.inventory.InventoryEntry
import dev.sporesync.settings.DownloadSettings
import java.security.MessageDigest

data class DownloadSpec(val settings: DownloadSettings, val entry: InventoryEntry) {
  fun identity(): String =
      MessageDigest.getInstance("SHA-256")
          .digest(
              listOf(
                      settings.host,
                      settings.port,
                      settings.username,
                      settings.source,
                      settings.destination,
                      entry.path,
                      entry.sizeBytes,
                      entry.modifiedTimeNs,
                  )
                  .joinToString("\u0000")
                  .toByteArray()
          )
          .toHexString()
}
