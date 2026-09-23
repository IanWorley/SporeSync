package dev.sporesync.discovery.internal

import dev.sporesync.downloads.DownloadQueue
import dev.sporesync.downloads.DownloadSpec
import dev.sporesync.inventory.EntryType
import dev.sporesync.inventory.Inventory
import dev.sporesync.inventory.InventoryEntry
import dev.sporesync.inventory.InventoryException
import dev.sporesync.inventory.InventoryScanner
import dev.sporesync.settings.DEFAULT_SCAN_SECONDS
import dev.sporesync.settings.DownloadSettings
import dev.sporesync.settings.DownloadSettingsStore
import dev.sporesync.settings.MAX_SCAN_SECONDS
import dev.sporesync.settings.MIN_SCAN_SECONDS
import java.time.Instant
import org.springframework.beans.factory.annotation.Value
import org.springframework.scheduling.annotation.Scheduled
import org.springframework.stereotype.Service

private const val SCANNER_DIRECTORY = ".sporesync"
private const val DISCOVERY_POLL_MILLIS = 1000L
private const val MILLIS_PER_SECOND = 1000L

data class DiscoverySnapshot(
    val inventory: Inventory? = null,
    val lastAttempt: Instant? = null,
    val lastSuccess: Instant? = null,
    val error: String? = null,
)

@Service
class Discovery(
    private val remote: InventoryScanner,
    private val configuration: DownloadSettingsStore,
    private val queue: DownloadQueue,
    @param:Value("\${sporesync.background.enabled:true}") private val enabled: Boolean,
) {
  @Volatile private var snapshot = DiscoverySnapshot()
  private var previousSettings: DownloadSettings? = null
  private var previousEntries = emptyMap<String, InventoryEntry>()

  fun state(): DiscoverySnapshot = snapshot

  @Scheduled(fixedDelay = DISCOVERY_POLL_MILLIS)
  fun scheduled() {
    if (!enabled) return
    val interval =
        try {
          configuration.read().scanSeconds.coerceIn(MIN_SCAN_SECONDS, MAX_SCAN_SECONDS)
        } catch (_: Exception) {
          DEFAULT_SCAN_SECONDS
        }
    val last = snapshot.lastAttempt?.toEpochMilli() ?: 0
    if (System.currentTimeMillis() - last >= interval * MILLIS_PER_SECOND) {
      try {
        scan()
      } catch (_: Exception) {
        /* scan records a credential-safe error for the dashboard. */
      }
    }
  }

  @Synchronized
  fun scan(): Inventory {
    snapshot = snapshot.copy(lastAttempt = Instant.now(), error = null)
    try {
      val settings = configuration.read()
      if (settings.automatic) settings.validate() else settings.validateDiscovery()
      val connection = settings.connection()
      val inventory = remote.scan(connection)
      accept(settings, inventory)
      return inventory
    } catch (error: Exception) {
      previousEntries = emptyMap()
      snapshot =
          snapshot.copy(error = (error as? InventoryException)?.code?.name ?: "CONFIGURATION")
      throw error
    }
  }

  @Synchronized
  internal fun accept(settings: DownloadSettings, inventory: Inventory) {
    val stable = if (settings == previousSettings) previousEntries else emptyMap()
    if (settings.automatic) {
      inventory.entries
          .filter {
            it.type == EntryType.file &&
                it == stable[it.path] &&
                it.path.substringBefore('/') != SCANNER_DIRECTORY
          }
          .forEach { queue.acceptStable(DownloadSpec(settings, it)) }
    }
    previousSettings = settings
    previousEntries = inventory.entries.associateBy { it.path }
    snapshot = snapshot.copy(inventory = inventory, lastSuccess = Instant.now(), error = null)
  }
}
