package com.sporesync.persistence

/** How a finished transfer ended. Stored by name, so renaming a constant needs a migration. */
enum class TransferOutcome {
    COMPLETED,
    FAILED,
}
