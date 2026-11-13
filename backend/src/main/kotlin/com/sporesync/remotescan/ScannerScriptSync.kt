package com.sporesync.remotescan

import org.springframework.stereotype.Component

private const val SCRIPT_NAME = "scanner.py"
private const val HOME = "~"

data class ScannerScriptSyncResult(
    val remotePath: String,
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
        remoteScannerPath: String,
    ): ScannerScriptSyncResult {
        require(remoteScannerPath.isNotBlank()) { "remoteScannerPath must not be blank" }
        val remotePath = scriptPath(remoteScannerPath)
        ensureParentDirectory(connection, remotePath)

        val remoteBytes = connection.readFile(remotePath)
        val remoteHash = remoteBytes?.sha256Hex()
        if (remoteHash == script.sha256) {
            return ScannerScriptSyncResult(
                remotePath = remotePath,
                uploaded = false,
                localHash = script.sha256,
                remoteHashBeforeUpload = remoteHash,
            )
        }

        connection.writeFile(remotePath, script.bytes)
        return ScannerScriptSyncResult(
            remotePath = remotePath,
            uploaded = true,
            localHash = script.sha256,
            remoteHashBeforeUpload = remoteHash,
        )
    }

    // SFTP and SSH exec both start in the remote home directory, and neither expands `~`.
    private fun scriptPath(remoteScannerPath: String): String {
        val trimmed = remoteScannerPath.trimEnd('/')
        val path = if (trimmed == HOME) "." else trimmed.removePrefix("$HOME/")
        return if (path.substringAfterLast('/') == SCRIPT_NAME) path else "$path/$SCRIPT_NAME"
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
