package com.sporesync.transfer.internal

import com.sporesync.config.AuthMethod
import com.sporesync.config.ConnectionSettings
import net.schmizz.sshj.SSHClient
import net.schmizz.sshj.common.DisconnectReason
import net.schmizz.sshj.sftp.SFTPClient
import net.schmizz.sshj.transport.TransportException
import net.schmizz.sshj.transport.verification.OpenSSHKnownHosts
import org.slf4j.LoggerFactory
import java.io.IOException
import java.nio.file.Path
import java.time.Duration
import java.util.concurrent.ConcurrentLinkedDeque

private const val CONNECT_TIMEOUT_SECONDS = 30L
private val CONNECT_TIMEOUT: Duration = Duration.ofSeconds(CONNECT_TIMEOUT_SECONDS)
private const val SOCKET_TIMEOUT_SECONDS = 30L
private val SOCKET_TIMEOUT: Duration = Duration.ofSeconds(SOCKET_TIMEOUT_SECONDS)
private const val NOT_CONFIGURED_MESSAGE = "SSH connection is not configured"

private val log = LoggerFactory.getLogger(SshConnection::class.java)

/** One authenticated SSH connection with an open SFTP channel, tagged with the settings it was opened with. */
internal class SshConnection private constructor(
    val settings: ConnectionSettings,
    private val client: SSHClient,
    val sftp: SFTPClient,
) : AutoCloseable {
    val isConnected: Boolean get() = client.isConnected

    /** Never throws: connections are closed in `finally` blocks, where an error would hide the original one. */
    override fun close() {
        try {
            client.use { sftp.close() }
        } catch (e: IOException) {
            log.debug("Ignoring error while closing SSH connection to {}", settings.host, e)
        }
    }

    companion object {
        fun open(
            settings: ConnectionSettings,
            knownHostsFile: Path,
        ): SshConnection {
            check(settings.isConfigured) { NOT_CONFIGURED_MESSAGE }
            val client = SSHClient()
            try {
                // SSHJ treats a missing file as empty and rejects unknown or changed keys.
                client.addHostKeyVerifier(OpenSSHKnownHosts(knownHostsFile.toFile()))
                client.connectTimeout = CONNECT_TIMEOUT.toMillis().toInt()
                client.timeout = SOCKET_TIMEOUT.toMillis().toInt()
                connect(client, settings, knownHostsFile)
                when (settings.authMethod) {
                    AuthMethod.PASSWORD -> {
                        client.authPassword(settings.username, settings.password.orEmpty())
                    }

                    AuthMethod.KEY -> {
                        val keyPath = checkNotNull(settings.keyPath) { NOT_CONFIGURED_MESSAGE }
                        client.authPublickey(settings.username, client.loadKeys(keyPath.toString()))
                    }
                }
                return SshConnection(settings, client, client.newSFTPClient())
            } catch (e: IOException) {
                client.close()
                throw e
            }
        }

        private fun connect(
            client: SSHClient,
            settings: ConnectionSettings,
            knownHostsFile: Path,
        ) {
            try {
                client.connect(settings.host, settings.port)
            } catch (e: TransportException) {
                if (e.disconnectReason != DisconnectReason.HOST_KEY_NOT_VERIFIABLE) throw e
                throw IOException("${e.message}. To trust this server, add its host key to $knownHostsFile", e)
            }
        }
    }
}

/** Keeps up to the current transfer concurrency limit of idle SSH connections for reuse. */
internal class SshConnectionPool(
    private val connect: (ConnectionSettings) -> SshConnection,
    private val maxIdleConnections: () -> Int,
) : AutoCloseable {
    private val idle = ConcurrentLinkedDeque<SshConnection>()
    private val lock = Any()

    private var closed = false

    /** Reuses an idle connection opened with the same [settings], closing stale ones on the way. */
    fun borrow(settings: ConnectionSettings): SshConnection {
        while (true) {
            val connection = synchronized(lock) { idle.pollFirst() } ?: return connect(settings)
            if (connection.settings == settings && connection.isConnected) return connection
            connection.close()
        }
    }

    /** Returns [connection] for reuse, or closes it when it may be broken, for example after a stop or an error. */
    fun release(
        connection: SshConnection,
        reusable: Boolean,
    ) {
        val toClose = mutableListOf<SshConnection>()
        synchronized(lock) {
            if (!reusable || closed) {
                toClose += connection
            } else {
                idle.offerFirst(connection)
                while (idle.size > maxIdleConnections()) {
                    idle.pollLast()?.let(toClose::add)
                }
            }
        }
        toClose.forEach(SshConnection::close)
    }

    override fun close() {
        val toClose =
            synchronized(lock) {
                closed = true
                generateSequence { idle.pollFirst() }.toList()
            }
        toClose.forEach(SshConnection::close)
    }
}
