package com.sporesync.remotescan.internal

import com.sporesync.remotescan.SshAuthMethod
import com.sporesync.remotescan.SshCommandResult
import com.sporesync.remotescan.SshConnectionConfig
import com.sporesync.remotescan.SshSftpConnection
import com.sporesync.remotescan.SshSftpConnectionFactory
import net.schmizz.sshj.SSHClient
import net.schmizz.sshj.sftp.OpenMode
import net.schmizz.sshj.sftp.RenameFlags
import net.schmizz.sshj.sftp.Response.StatusCode.NO_SUCH_FILE
import net.schmizz.sshj.sftp.Response.StatusCode.OP_UNSUPPORTED
import net.schmizz.sshj.sftp.SFTPClient
import net.schmizz.sshj.sftp.SFTPException
import net.schmizz.sshj.transport.verification.HostKeyVerifier
import net.schmizz.sshj.transport.verification.OpenSSHKnownHosts
import org.springframework.stereotype.Component
import java.io.IOException
import java.security.MessageDigest
import java.security.PublicKey
import java.util.Base64
import java.util.UUID
import java.util.concurrent.ExecutorService
import java.util.concurrent.ExecutionException
import java.util.concurrent.Executors
import java.util.concurrent.Future
import java.util.concurrent.TimeUnit
import java.util.concurrent.TimeoutException

private const val SHA_256 = "SHA-256"
private const val SHA_256_PREFIX = "SHA256:"
private const val COMMAND_TIMEOUT_SECONDS = 60L
private const val DRAIN_THREAD_PREFIX = "remotescan-drain-"
private val COMMAND_TIMEOUT: Long = TimeUnit.SECONDS.toMillis(COMMAND_TIMEOUT_SECONDS)

@Component
internal class SshjSshSftpConnectionFactory : SshSftpConnectionFactory {
    override fun open(config: SshConnectionConfig): SshSftpConnection {
        val client = SSHClient()
        return runCatching {
            client.configureHostKeyVerification(config)
            client.connect(config.host, config.port)
            when (config.authMethod) {
                SshAuthMethod.PASSWORD -> {
                    client.authPassword(config.username, requireNotNull(config.password))
                }

                SshAuthMethod.KEY -> {
                    val keyPath = requireNotNull(config.keyPath)
                    client.authPublickey(config.username, client.loadKeys(keyPath.toString()))
                }
            }
            SshjSshSftpConnection(client, client.newSFTPClient())
        }.onFailure {
            client.closeSilently()
        }.getOrThrow()
    }

    private fun SSHClient.configureHostKeyVerification(config: SshConnectionConfig) {
        val expectedFingerprint = config.hostKeyFingerprint?.trim()?.takeIf(String::isNotEmpty)
        if (expectedFingerprint != null) {
            val normalized =
                if (expectedFingerprint.startsWith(SHA_256_PREFIX)) {
                    expectedFingerprint
                } else {
                    SHA_256_PREFIX + expectedFingerprint
                }
            addHostKeyVerifier(FingerprintHostKeyVerifier(normalized))
            return
        }
        val knownHostsFile = requireNotNull(config.knownHostsFile)
        addHostKeyVerifier(OpenSSHKnownHosts(knownHostsFile.toFile()))
    }
}

private class FingerprintHostKeyVerifier(
    private val expectedFingerprint: String,
) : HostKeyVerifier {
    override fun verify(
        hostname: String,
        port: Int,
        key: PublicKey,
    ): Boolean = hostKeyFingerprintSha256(key) == expectedFingerprint

    override fun findExistingAlgorithms(
        hostname: String,
        port: Int,
    ): List<String> = emptyList()
}

private class SshjSshSftpConnection(
    private val client: SSHClient,
    private val sftp: SFTPClient,
) : SshSftpConnection {
    override fun run(command: String): SshCommandResult {
        return client
            .startSession()
            .use { session ->
                session
                    .exec(command)
                    .use { process ->
                        val stdout = drainExecutor.submit<ByteArray> { process.inputStream.readAllBytes() }
                        val stderr = drainExecutor.submit<ByteArray> { process.errorStream.readAllBytes() }
                        val joined = drainExecutor.submit<Unit> { process.join() }
                        try {
                            joined.get(COMMAND_TIMEOUT, TimeUnit.MILLISECONDS)
                            val stdoutText = stdout.await("stdout").decodeToString()
                            val stderrText = stderr.await("stderr").decodeToString()
                            val exitCode = process.exitStatus ?: -1
                            SshCommandResult(exitCode = exitCode, stdout = stdoutText, stderr = stderrText)
                        } catch (e: TimeoutException) {
                            process.closeSilently()
                            throw IOException("SSH command timed out after ${COMMAND_TIMEOUT_SECONDS}s: $command", e)
                        } catch (e: InterruptedException) {
                            process.closeSilently()
                            Thread.currentThread().interrupt()
                            throw IOException("SSH command interrupted: $command", e)
                        } catch (e: ExecutionException) {
                            process.closeSilently()
                            throw IOException("SSH command failed: $command", e)
                        } finally {
                            stdout.cancel(true)
                            stderr.cancel(true)
                            joined.cancel(true)
                        }
                    }
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
        val tempPath = "$path.tmp-${UUID.randomUUID()}"
        try {
            sftp
                .open(tempPath, setOf(OpenMode.CREAT, OpenMode.TRUNC, OpenMode.WRITE))
                .use { remoteFile ->
                    remoteFile.RemoteFileOutputStream(0L).use { output -> output.write(content) }
                }
            renameWithOverwrite(tempPath, path)
        } catch (e: IOException) {
            try {
                sftp.rm(tempPath)
            } catch (_: SFTPException) {
            }
            throw e
        }
    }

    private fun renameWithOverwrite(
        source: String,
        target: String,
    ) {
        try {
            sftp.rename(source, target, setOf(RenameFlags.OVERWRITE, RenameFlags.ATOMIC))
        } catch (e: SFTPException) {
            if (e.statusCode != OP_UNSUPPORTED) throw e
            sftp.rename(source, target, setOf(RenameFlags.OVERWRITE))
        }
    }

    override fun close() {
        client.use { sftp.close() }
    }

    private fun Future<ByteArray>.await(streamName: String): ByteArray =
        try {
            get(COMMAND_TIMEOUT, TimeUnit.MILLISECONDS)
        } catch (e: TimeoutException) {
            throw IOException("Timed out draining $streamName after ${COMMAND_TIMEOUT_SECONDS}s", e)
        }

    companion object {
        private val drainExecutor: ExecutorService =
            Executors.newCachedThreadPool { runnable ->
                Thread(runnable, DRAIN_THREAD_PREFIX + UUID.randomUUID()).apply { isDaemon = true }
            }
    }
}

private fun hostKeyFingerprintSha256(key: PublicKey): String {
    val digest = MessageDigest.getInstance(SHA_256).digest(key.encoded)
    val encoded = Base64.getEncoder().withoutPadding().encodeToString(digest)
    return SHA_256_PREFIX + encoded
}

private fun SSHClient.closeSilently() {
    try {
        close()
    } catch (_: Exception) {
    }
}

private fun AutoCloseable.closeSilently() {
    try {
        close()
    } catch (_: Exception) {
    }
}
