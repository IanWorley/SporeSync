package dev.sporesync.model.inventory

enum class EntryType {
  file,
  directory,
  symlink,
  other,
}

data class InventoryEntry(
    val path: String,
    val type: EntryType,
    val sizeBytes: Long,
    val modifiedTimeNs: Long,
)

data class Inventory(val schemaVersion: Int, val entries: List<InventoryEntry>)

enum class InventoryFailure {
  CONFIGURATION,
  CONNECTION,
  AUTHENTICATION,
  UPLOAD,
  EXECUTION,
  TIMEOUT,
  PROTOCOL,
}

class InventoryException(val code: InventoryFailure) :
    RuntimeException("Remote inventory failed: $code")
