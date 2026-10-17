package com.sporesync.transfer.internal

import com.sporesync.config.AuthMethod
import com.sporesync.config.ConnectionSettings
import net.schmizz.sshj.SSHClient
import net.schmizz.sshj.sftp.SFTPClient
import net.schmizz.sshj.transport.verification.PromiscuousVerifier
import org.slf4j.LoggerFactory
import java.io.IOException
import java.time.Duration
import java.util.concurrent.ConcurrentLinkedDeque

private const val CONNECT_TIMEOUT_SECONDS = 30L
private val CONNECT_TIMEOUT: Duration = Duration.ofSeconds(CONNECT_TIMEOUT_SECONDS)
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
        fun open(settings: ConnectionSettings): SshConnection {
            check(settings.isConfigured) { NOT_CONFIGURED_MESSAGE }
            val client = SSHClient()
            try {
                // Parity with seedsync, which disables host key checking: SporeSync assumes a trusted network.
                client.addHostKeyVerifier(PromiscuousVerifier())
                client.connectTimeout = CONNECT_TIMEOUT.toMillis().toInt()
                client.connect(settings.host, settings.port)
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
    }
}

/**
 * Keeps idle SSH connections for reuse, so each transfer skips the handshake. The pool holds no upper bound of its
 * own: every connection is held by a transfer worker, and the queue already caps workers at the concurrent limit.
 */
internal class SshConnectionPool(
    private val connect: (ConnectionSettings) -> SshConnection = SshConnection::open,
) : AutoCloseable {
    private val idle = ConcurrentLinkedDeque<SshConnection>()

    @Volatile
    private var closed = false

    /** Reuses an idle connection opened with the same [settings], closing stale ones on the way. */
    fun borrow(settings: ConnectionSettings): SshConnection {
        while (true) {
            val connection = idle.pollFirst() ?: return connect(settings)
            if (connection.settings == settings && connection.isConnected) return connection
            connection.close()
        }
    }

    /** Returns [connection] for reuse, or closes it when it may be broken, for example after a stop or an error. */
    fun release(
        connection: SshConnection,
        reusable: Boolean,
    ) {
        if (reusable && !closed) idle.offerFirst(connection) else connection.close()
        // A close() racing with the offer above may have missed this connection.
        if (closed) evictIdle()
    }

    override fun close() {
        closed = true
        evictIdle()
    }

    private fun evictIdle() {
        generateSequence { idle.pollFirst() }.forEach { it.close() }
    }
}
