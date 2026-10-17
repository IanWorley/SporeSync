package com.sporesync.transfer.internal

import com.sporesync.config.SettingsStore
import net.schmizz.sshj.sftp.FileMode
import net.schmizz.sshj.sftp.SFTPClient
import org.springframework.stereotype.Component
import java.io.IOException
import java.io.InputStream
import java.nio.ByteBuffer
import java.nio.channels.FileChannel
import java.nio.file.Files
import java.nio.file.Path
import java.nio.file.StandardOpenOption

/** Bytes per read and write; also the granularity at which stop and the bandwidth cap take effect. */
private const val CHUNK_BYTES = 32 * 1024

/** SFTP read requests kept in flight, so throughput is not bound by one round trip per chunk. */
private const val READ_AHEAD_REQUESTS = 16

private const val REMOTE_SEPARATOR = "/"

/** A remote regular file, where it lands locally, and how much of it is already there. */
private data class PlannedFile(
    val remotePath: String,
    val localPath: Path,
    val size: Long,
    val resumeOffset: Long,
)

/**
 * Downloads a file or directory tree over SFTP on a pooled SSH connection. Files are written in place and resumed
 * from the existing local size, so a stopped or failed transfer picks up where it left off. A local file larger
 * than the remote one is treated as stale and downloaded again from the start.
 */
@Component
internal class SftpDownloader(
    private val settings: SettingsStore,
) : Downloader,
    AutoCloseable {
    private val pool = SshConnectionPool()
    private val limiter =
        BandwidthLimiter({
            settings.current.transfer.bandwidthLimit
                ?.toBytes()
        })

    override fun download(
        path: String,
        progress: ProgressTracker,
    ) {
        val current = settings.current
        val localRoot =
            current.paths.localDir
                .toAbsolutePath()
                .normalize()
        val connection = pool.borrow(current.connection)
        // Only a clean finish returns the connection: after an error or interrupt it may hold unread responses.
        var reusable = false
        try {
            val remotePath = current.paths.remoteDir.trimEnd('/') + REMOTE_SEPARATOR + path
            val files = plan(connection.sftp, remotePath, localRoot.resolve(path), localRoot)
            progress.start(files.sumOf { it.size }, files.sumOf { it.resumeOffset })
            files.forEach { download(connection.sftp, it, progress) }
            reusable = true
        } finally {
            pool.release(connection, reusable)
        }
    }

    override fun close() = pool.close()

    /** Walks the remote tree, creating local directories (including empty ones) as it goes. */
    private fun plan(
        sftp: SFTPClient,
        remoteRoot: String,
        localTarget: Path,
        localRoot: Path,
    ): List<PlannedFile> {
        val rootAttributes = sftp.stat(remoteRoot)
        if (rootAttributes.type == FileMode.Type.REGULAR) {
            Files.createDirectories(localTarget.parent)
            return listOf(planFile(remoteRoot, localTarget, rootAttributes.size))
        }
        if (rootAttributes.type != FileMode.Type.DIRECTORY) throw IOException("$remoteRoot is not a file or directory")

        val files = mutableListOf<PlannedFile>()
        val pending = ArrayDeque(listOf(remoteRoot to localTarget))
        while (pending.isNotEmpty()) {
            val (remoteDir, localDir) = pending.removeFirst()
            Files.createDirectories(localDir)
            for (child in sftp.ls(remoteDir)) {
                val localChild = localDir.resolve(child.name).normalize()
                // A name from the server must never point outside the download directory.
                if (!localChild.startsWith(localRoot)) throw IOException("Remote name ${child.name} escapes $localRoot")
                when (child.attributes.type) {
                    FileMode.Type.DIRECTORY -> pending += child.path to localChild

                    FileMode.Type.REGULAR -> files += planFile(child.path, localChild, child.attributes.size)

                    // Symlinks and special files are not mirrored.
                    else -> Unit
                }
            }
        }
        return files
    }

    private fun planFile(
        remotePath: String,
        localPath: Path,
        size: Long,
    ): PlannedFile {
        val localSize = if (Files.isRegularFile(localPath)) Files.size(localPath) else 0L
        return PlannedFile(remotePath, localPath, size, resumeOffset = if (localSize <= size) localSize else 0L)
    }

    private fun download(
        sftp: SFTPClient,
        file: PlannedFile,
        progress: ProgressTracker,
    ) {
        if (file.resumeOffset == file.size && Files.exists(file.localPath)) return

        sftp.open(file.remotePath).use { remote ->
            val input = remote.ReadAheadRemoteFileInputStream(READ_AHEAD_REQUESTS, file.resumeOffset)
            input.use {
                FileChannel.open(file.localPath, StandardOpenOption.CREATE, StandardOpenOption.WRITE).use { output ->
                    output.truncate(file.resumeOffset)
                    output.position(file.resumeOffset)
                    copy(input, output, progress)
                }
            }
        }
    }

    private fun copy(
        input: InputStream,
        output: FileChannel,
        progress: ProgressTracker,
    ) {
        val buffer = ByteArray(CHUNK_BYTES)
        while (true) {
            if (Thread.currentThread().isInterrupted) throw InterruptedException()
            val read = input.read(buffer)
            if (read < 0) return
            limiter.acquire(read)
            output.write(ByteBuffer.wrap(buffer, 0, read))
            progress.advance(read)
        }
    }
}
