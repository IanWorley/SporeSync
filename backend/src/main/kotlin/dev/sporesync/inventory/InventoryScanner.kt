package dev.sporesync.inventory

import dev.sporesync.settings.SshConnectionSettings

/** Discovers remote entries using a single connection snapshot. */
interface InventoryScanner {
  fun scan(connection: SshConnectionSettings): Inventory
}
