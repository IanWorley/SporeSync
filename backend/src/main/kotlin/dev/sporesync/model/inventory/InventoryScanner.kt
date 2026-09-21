package dev.sporesync.model.inventory

/** Discovers remote entries using a single connection snapshot. */
interface InventoryScanner {
  fun scan(): Inventory
}
