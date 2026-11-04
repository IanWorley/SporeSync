package com.sporesync.remotescan.internal

import com.sporesync.remotescan.SshAuthMethod
import com.sporesync.remotescan.SshCommandResult
import com.sporesync.remotescan.SshConnectionConfig
import com.sporesync.remotescan.SshSftpConnection
import com.sporesync.remotescan.SshSftpConnectionFactory
import net.schmizz.sshj.SSHClient
import net.schmizz.sshj.sftp.OpenMode
import net.schmizz.sshj.sftp.Response.StatusCode.NO_SUCH_FILE
import net.schmizz.sshj.sftp.SFTPClient
import net.schmizz.sshj.sftp.SFTPException
import net.schmizz.sshj.transport.verification.PromiscuousVerifier
import org.springframework.stereotype.Component
import java.io.IOException
import java.nio.charset.StandardCharsets

@Component
internal class SshjSshSftpConnectionFactory : SshSftpConnectionFactory {
    override fun open(config: SshConnectionConfig): SshSftpConnection {
        val client = SSHClient()
        try {
            client.addHostKeyVerifier(PromiscuousVerifier())
            client.connect(config.host, config.port)
            when (config.authMethod) {
                SshAuthMethod.PASSWORD ->
                    client.authPassword(config.username, requireNotNull(config.password))

                SshAuthMethod.KEY -> {
                    val keyPath = requireNotNull(config.keyPath)
                    client.authPublickey(config.username, client.loadKeys(keyPath.toString()))
                }
            }
            return SshjSshSftpConnection(client, client.newSFTPClient())
        } catch (e: IOException) {
            client.close()
            throw e
        }
    }
}

private class SshjSshSftpConnection(
    private val client: SSHClient,
    private val sftp: SFTPClient,
) : SshSftpConnection {
    override fun run(command: String): SshCommandResult =
        client
            .startSession()
            .use { session ->
                session
                    .exec(command)
                    .use { process ->
                        process.join()
                        val stdout = process.inputStream.readAllBytes().toString(StandardCharsets.UTF_8)
                        val stderr = process.errorStream.readAllBytes().toString(StandardCharsets.UTF_8)
                        val exitCode = process.exitStatus ?: -1
                        SshCommandResult(exitCode = exitCode, stdout = stdout, stderr = stderr)
                    }
            }

    override fun readFile(path: String): ByteArray? =
        try {
            sftp
                .open(path)
                .use { remoteFile ->
                    remoteFile.RemoteFileInputStream(0L).use { input -> input.readAllBytes() }
                }
        } catch (e: SFTPException) {
            if (e.statusCode == NO_SUCH_FILE) {
                null
            } else {
                throw e
            }
        }

    override fun writeFile(
        path: String,
        content: ByteArray,
    ) {
        sftp
            .open(path, setOf(OpenMode.CREAT, OpenMode.TRUNC, OpenMode.WRITE))
            .use { remoteFile ->
                remoteFile.RemoteFileOutputStream(0L).use { output -> output.write(content) }
            }
    }

    override fun close() {
        client.use { sftp.close() }
    }
}
