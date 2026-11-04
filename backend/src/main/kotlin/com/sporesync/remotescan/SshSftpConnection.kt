package com.sporesync.remotescan

import java.io.IOException

data class SshCommandResult(
    val exitCode: Int,
    val stdout: String,
    val stderr: String,
) {
    fun requireSuccess(command: String) {
        if (exitCode == 0) return
        throw IOException("SSH command failed ($exitCode): $command\n$stderr")
    }
}

interface SshSftpConnection : AutoCloseable {
    fun run(command: String): SshCommandResult

    fun readFile(path: String): ByteArray?

    fun writeFile(
        path: String,
        content: ByteArray,
    )
}

interface SshSftpConnectionFactory {
    fun open(config: SshConnectionConfig): SshSftpConnection
}
