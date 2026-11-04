package com.sporesync.remotescan

import org.springframework.stereotype.Component

data class ScannerScriptSyncResult(
    val uploaded: Boolean,
    val localHash: String,
    val remoteHashBeforeUpload: String?,
)

@Component
class ScannerScriptSync(
    private val script: EmbeddedScannerScript,
) {
    fun ensureCurrent(
        connection: SshSftpConnection,
        remotePath: String,
    ): ScannerScriptSyncResult {
        require(remotePath.isNotBlank()) { "remotePath must not be blank" }
        ensureParentDirectory(connection, remotePath)

        val remoteBytes = connection.readFile(remotePath)
        val remoteHash = remoteBytes?.sha256Hex()
        if (remoteHash == script.sha256) {
            return ScannerScriptSyncResult(
                uploaded = false,
                localHash = script.sha256,
                remoteHashBeforeUpload = remoteHash,
            )
        }

        connection.writeFile(remotePath, script.bytes)
        return ScannerScriptSyncResult(
            uploaded = true,
            localHash = script.sha256,
            remoteHashBeforeUpload = remoteHash,
        )
    }

    private fun ensureParentDirectory(
        connection: SshSftpConnection,
        remotePath: String,
    ) {
        val parent = remotePath.substringBeforeLast('/', "")
        if (parent.isEmpty()) return
        connection.run("mkdir -p ${shellQuote(parent)}").requireSuccess("mkdir -p $parent")
    }

    private fun shellQuote(value: String): String = "'" + value.replace("'", "'\"'\"'") + "'"
}
