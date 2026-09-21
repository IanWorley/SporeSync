package dev.sporesync.model.download

import dev.sporesync.config.SshSettings
import dev.sporesync.model.inventory.EntryType
import java.nio.ByteBuffer
import java.security.MessageDigest
import net.schmizz.sshj.SSHClient
import net.schmizz.sshj.sftp.OpenMode
import org.springframework.stereotype.Service

private const val COPY_BUFFER_BYTES = 64 * 1024
private const val PIPELINED_READS = 16

class DownloadFailure(val code: String) : RuntimeException(code)

@Service
class SftpDownload(private val credentials: SshSettings, private val storage: DownloadStorage) :
    FileDownloader {
  override fun transfer(spec: DownloadSpec, progress: (Long) -> Unit, cancelled: () -> Boolean) {
    spec.settings.validate()
    require(spec.entry.type == EntryType.file && spec.entry.sizeBytes >= 0)
    storage.open(spec.settings.destination, spec.entry.path, spec.settings.temporaryFiles).use {
        target ->
      val local = target.file
      val initialSize = local.size()
      if (initialSize > spec.entry.sizeBytes) throw DownloadFailure("LOCAL_CONFLICT")
      val digest = MessageDigest.getInstance("SHA-256")
      val buffer = ByteArray(COPY_BUFFER_BYTES)
      local.position(0)
      while (local.position() < initialSize) {
        if (cancelled()) throw DownloadFailure("CANCELLED")
        val count =
            local.read(
                ByteBuffer.wrap(
                    buffer,
                    0,
                    minOf(buffer.size.toLong(), initialSize - local.position()).toInt(),
                )
            )
        if (count <= 0) throw DownloadFailure("LOCAL_CONFLICT")
        digest.update(buffer, 0, count)
      }
      val prefix = (digest.clone() as MessageDigest).digest().toHexString()
      SSHClient().use { ssh ->
        credentials.validate()
        ssh.connectTimeout = spec.settings.timeoutMillis
        ssh.timeout = spec.settings.timeoutMillis
        ssh.transport.timeoutMs = spec.settings.timeoutMillis
        ssh.connection.timeoutMs = spec.settings.timeoutMillis
        ssh.loadKnownHosts(java.nio.file.Path.of(credentials.knownHosts).toFile())
        val key = ssh.loadKeys(credentials.privateKey, credentials.passphrase)
        ssh.connect(spec.settings.host, spec.settings.port)
        ssh.authPublickey(spec.settings.username, key)
        val before = RemoteChecksum.read(ssh, spec, initialSize, cancelled)
        if (before.prefix != prefix) throw DownloadFailure("LOCAL_CONFLICT")
        var position = initialSize
        ssh.newSFTPClient().use { sftp ->
          sftp.sftpEngine.timeoutMs = spec.settings.timeoutMillis
          val source = spec.settings.source.trimEnd('/') + "/" + spec.entry.path
          val root = sftp.canonicalize(spec.settings.source).trimEnd('/')
          if (sftp.canonicalize(source) != "$root/${spec.entry.path}")
              throw DownloadFailure("REMOTE_SYMLINK")
          sftp.open(source, setOf(OpenMode.READ)).use { remote ->
            remote
                .ReadAheadRemoteFileInputStream(
                    PIPELINED_READS,
                    initialSize,
                    spec.entry.sizeBytes - initialSize,
                )
                .use { input ->
                  while (position < spec.entry.sizeBytes) {
                    if (cancelled()) throw DownloadFailure("CANCELLED")
                    val count =
                        input.read(
                            buffer,
                            0,
                            minOf(buffer.size.toLong(), spec.entry.sizeBytes - position).toInt(),
                        )
                    if (count <= 0) throw DownloadFailure("REMOTE_CHANGED")
                    val bytes = ByteBuffer.wrap(buffer, 0, count)
                    while (bytes.hasRemaining()) local.write(bytes)
                    digest.update(buffer, 0, count)
                    position += count
                    progress(position)
                  }
                }
          }
        }
        local.force()
        if (cancelled()) throw DownloadFailure("CANCELLED")
        if (
            digest.digest().toHexString() != before.complete ||
                RemoteChecksum.read(ssh, spec, initialSize, cancelled) != before
        )
            throw DownloadFailure("REMOTE_CHANGED")
      }
      if (cancelled()) throw DownloadFailure("CANCELLED")
      target.publish()
      progress(spec.entry.sizeBytes)
    }
  }
}
