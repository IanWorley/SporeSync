package dev.sporesync.model.inventory

import dev.sporesync.config.SshConnectionSettings

/** Discovers remote entries using a single connection snapshot. */
interface InventoryScanner {
  fun scan(connectionOverride: SshConnectionSettings? = null): Inventory
}
