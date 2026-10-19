package com.sporesync.transfer.internal

import com.sporesync.config.SettingsStore
import net.schmizz.sshj.sftp.FileMode
import net.schmizz.sshj.sftp.SFTPClient
import org.springframework.beans.factory.annotation.Value
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

private const val KNOWN_HOSTS_FILE_PROPERTY = "sporesync.known-hosts-file"

private const val DEFAULT_KNOWN_HOSTS_FILE = "/config/known_hosts"

private data class PlannedFile(
    val remotePath: String,
    val localPath: Path,
    val size: Long,
    val localSize: Long?,
) {
    val resumeOffset: Long get() = localSize?.takeIf { it <= size } ?: 0L
}

/**
 * Downloads a file or directory tree over SFTP on a pooled SSH connection. Files are written in place and resumed
 * from the existing local size, so a stopped or failed transfer picks up where it left off. A local file larger
 * than the remote one is treated as stale and downloaded again from the start.
 */
@Component
internal class SftpDownloader(
    private val settings: SettingsStore,
    @Value("\${$KNOWN_HOSTS_FILE_PROPERTY:$DEFAULT_KNOWN_HOSTS_FILE}") knownHostsFile: Path,
) : Downloader,
    AutoCloseable {
    private val pool =
        SshConnectionPool(
            { SshConnection.open(it, knownHostsFile) },
            { settings.current.transfer.concurrentFileLimit },
        )
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
            val localTarget = localRoot.resolve(path).normalize()
            requireLocalChild(localTarget, path, localRoot)
            validateRemoteAncestors(connection.sftp, current.paths.remoteDir, path)
            val files = plan(connection.sftp, remotePath, localTarget, localRoot)
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
        val rootAttributes = sftp.lstat(remoteRoot)
        if (rootAttributes.type == FileMode.Type.REGULAR) {
            Files.createDirectories(localTarget.parent)
            return listOf(planFile(remoteRoot, localTarget, rootAttributes.size))
        }
        if (rootAttributes.type != FileMode.Type.DIRECTORY) {
            throw IOException("$remoteRoot is not a regular file or directory")
        }

        val files = mutableListOf<PlannedFile>()
        val pending = ArrayDeque(listOf(remoteRoot to localTarget))
        while (pending.isNotEmpty()) {
            checkNotInterrupted()
            val (remoteDir, localDir) = pending.removeFirst()
            Files.createDirectories(localDir)
            for (child in sftp.ls(remoteDir)) {
                checkNotInterrupted()
                val localChild = localDir.resolve(child.name).normalize()
                // A name from the server must never point outside the download directory.
                requireLocalChild(localChild, child.name, localRoot)
                when (child.attributes.type) {
                    FileMode.Type.DIRECTORY -> pending += child.path to localChild

                    FileMode.Type.REGULAR -> files += planFile(child.path, localChild, child.attributes.size)

                    FileMode.Type.SYMLINK,
                    FileMode.Type.SOCKET_SPECIAL,
                    FileMode.Type.CHAR_SPECIAL,
                    FileMode.Type.BLOCK_SPECIAL,
                    FileMode.Type.FIFO_SPECIAL,
                    -> Unit

                    FileMode.Type.UNKNOWN -> throw IOException("Could not determine type of remote entry ${child.path}")
                }
            }
        }
        return files
    }

    private fun requireLocalChild(
        localChild: Path,
        remoteName: String,
        localRoot: Path,
    ) {
        if (!localChild.startsWith(localRoot)) throw IOException("Remote name $remoteName escapes $localRoot")
    }

    private fun checkNotInterrupted() {
        if (Thread.currentThread().isInterrupted) throw InterruptedException()
    }

    private fun validateRemoteAncestors(
        sftp: SFTPClient,
        remoteRoot: String,
        relativePath: String,
    ) {
        var ancestor = remoteRoot.trimEnd('/')
        for (segment in relativePath.split(REMOTE_SEPARATOR).dropLast(1)) {
            checkNotInterrupted()
            ancestor += REMOTE_SEPARATOR + segment
            val type = sftp.lstat(ancestor).type
            if (type == FileMode.Type.SYMLINK) throw IOException("Remote path ancestor $ancestor is a symlink")
            if (type != FileMode.Type.DIRECTORY) throw IOException("Remote path ancestor $ancestor is not a directory")
        }
    }

    private fun planFile(
        remotePath: String,
        localPath: Path,
        size: Long,
    ): PlannedFile {
        val localSize = if (Files.isRegularFile(localPath)) Files.size(localPath) else null
        return PlannedFile(remotePath, localPath, size, localSize)
    }

    private fun download(
        sftp: SFTPClient,
        file: PlannedFile,
        progress: ProgressTracker,
    ) {
        FileChannel.open(file.localPath, StandardOpenOption.CREATE, StandardOpenOption.WRITE).use { output ->
            val resumeOffset = output.size().takeIf { it <= file.size } ?: 0L
            progress.reconcileResume(file.resumeOffset, resumeOffset)
            output.truncate(resumeOffset)
            if (resumeOffset < file.size) {
                copyFile(sftp, file, output, resumeOffset, progress)
            }
        }
        val downloaded = Files.size(file.localPath)
        if (downloaded != file.size) {
            throw IOException("${file.remotePath} changed size during download: $downloaded of ${file.size} bytes")
        }
    }

    private fun copyFile(
        sftp: SFTPClient,
        file: PlannedFile,
        output: FileChannel,
        resumeOffset: Long,
        progress: ProgressTracker,
    ) {
        sftp.open(file.remotePath).use { remote ->
            val input = remote.ReadAheadRemoteFileInputStream(READ_AHEAD_REQUESTS, resumeOffset)
            input.use {
                output.position(resumeOffset)
                copy(input, output, progress)
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
            val chunk = ByteBuffer.wrap(buffer, 0, read)
            while (chunk.hasRemaining()) output.write(chunk)
            progress.advance(read)
        }
    }
}
