package com.sporesync.persistence

import org.springframework.beans.factory.annotation.Value
import org.springframework.boot.context.event.ApplicationReadyEvent
import org.springframework.context.event.EventListener
import org.springframework.stereotype.Component
import java.nio.file.FileSystems
import java.nio.file.Files
import java.nio.file.Path
import java.nio.file.attribute.PosixFilePermission

@Component
class DatabaseFilePermissions(
    @Value("\${sporesync.persistence.directory}") private val directory: String,
    @Value("\${sporesync.persistence.database-name}") private val databaseName: String,
) {
    @EventListener(ApplicationReadyEvent::class)
    fun restrictMainDatabaseFileToOwnerOnly() {
        if (!FileSystems.getDefault().supportedFileAttributeViews().contains("posix")) {
            return
        }

        val databaseFile = Path.of(directory).resolve("$databaseName.mv.db")
        if (Files.exists(databaseFile)) {
            Files.setPosixFilePermissions(databaseFile, OWNER_ONLY_FILE_PERMISSIONS)
        }
    }

    private companion object {
        val OWNER_ONLY_FILE_PERMISSIONS = setOf(
            PosixFilePermission.OWNER_READ,
            PosixFilePermission.OWNER_WRITE,
        )
    }
}
