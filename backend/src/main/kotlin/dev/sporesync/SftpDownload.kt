package dev.sporesync

import java.nio.ByteBuffer
import java.nio.channels.FileChannel
import java.nio.file.Files
import java.nio.file.LinkOption.NOFOLLOW_LINKS
import java.nio.file.Path
import java.nio.file.StandardCopyOption.ATOMIC_MOVE
import java.nio.file.StandardOpenOption.*
import java.security.MessageDigest
import net.schmizz.sshj.SSHClient
import net.schmizz.sshj.sftp.OpenMode
import net.schmizz.sshj.sftp.RemoteFile
import org.springframework.stereotype.Service

private const val COPY_BUFFER_BYTES = 64 * 1024
private const val NANOS_PER_SECOND = 1_000_000_000L
private const val STAGING_DIRECTORY = ".sporesync"

class DownloadFailure(val code: String) : RuntimeException(code)

@Service
class SftpDownload(private val credentials: SshSettings) {
  fun transfer(spec: DownloadSpec, progress: (Long) -> Unit, cancelled: () -> Boolean) {
    spec.settings.validate()
    val entry = spec.entry
    require(entry.type == EntryType.file)
    val configuredRoot = Path.of(spec.settings.destination).normalize()
    Files.createDirectories(configuredRoot)
    // Resolve the explicitly configured root once; only descendants must be link-free.
    val root = configuredRoot.toRealPath()
    val target = safeTarget(root, entry.path)
    val lockPath = safeTarget(root, "$STAGING_DIRECTORY/worker.lock", internal = true)
    FileChannel.open(lockPath, CREATE, WRITE, NOFOLLOW_LINKS).use { lockChannel ->
      val lock = lockChannel.tryLock() ?: throw DownloadFailure("DESTINATION_BUSY")
      lock.use {
        val staging =
            if (spec.settings.temporaryFiles && !Files.exists(target, NOFOLLOW_LINKS)) {
              // Path-based staging survives growth and new inventory versions without name
              // collisions.
              val key =
                  MessageDigest.getInstance("SHA-256")
                      .digest(entry.path.toByteArray())
                      .toHexString()
              safeTarget(root, "$STAGING_DIRECTORY/$key.part", internal = true)
            } else target
        SSHClient().use { ssh ->
          credentials.validate()
          ssh.connectTimeout = spec.settings.timeoutMillis
          ssh.timeout = spec.settings.timeoutMillis
          ssh.loadKnownHosts(Path.of(credentials.knownHosts).toFile())
          val key = ssh.loadKeys(credentials.privateKey, credentials.passphrase)
          ssh.connect(spec.settings.host, spec.settings.port)
          ssh.authPublickey(spec.settings.username, key)
          ssh.newSFTPClient().use { sftp ->
            val source = spec.settings.source.trimEnd('/') + "/" + entry.path
            val canonicalRoot = sftp.canonicalize(spec.settings.source).trimEnd('/')
            if (sftp.canonicalize(source) != "$canonicalRoot/${entry.path}")
                throw DownloadFailure("REMOTE_SYMLINK")
            sftp.open(source, setOf(OpenMode.READ)).use { remote ->
              fun unchanged() {
                val attributes = remote.fetchAttributes()
                if (
                    attributes.size != entry.sizeBytes ||
                        attributes.mtime != entry.modifiedTimeNs / NANOS_PER_SECOND
                ) {
                  throw DownloadFailure("REMOTE_CHANGED")
                }
              }
              unchanged()
              FileChannel.open(staging, CREATE, READ, WRITE, NOFOLLOW_LINKS).use { local ->
                if (local.size() > entry.sizeBytes) throw DownloadFailure("LOCAL_CONFLICT")
                val initialSize = local.size()
                val digest = MessageDigest.getInstance("SHA-256")
                val buffer = ByteArray(COPY_BUFFER_BYTES)
                var position = 0L
                while (position < entry.sizeBytes) {
                  if (cancelled()) throw DownloadFailure("CANCELLED")
                  val length = minOf(buffer.size.toLong(), entry.sizeBytes - position).toInt()
                  val count = remote.read(position, buffer, 0, length)
                  if (count <= 0) throw DownloadFailure("REMOTE_CHANGED")
                  val existing =
                      minOf(count.toLong(), (initialSize - position).coerceAtLeast(0)).toInt()
                  if (existing > 0) {
                    val prefix = ByteBuffer.allocate(existing)
                    while (prefix.hasRemaining()) {
                      if (local.read(prefix, position + prefix.position()) <= 0)
                          throw DownloadFailure("LOCAL_CONFLICT")
                    }
                    if (!prefix.array().contentEquals(buffer.copyOf(existing)))
                        throw DownloadFailure("LOCAL_CONFLICT")
                  }
                  if (existing < count) {
                    val bytes = ByteBuffer.wrap(buffer, existing, count - existing)
                    local.position(position + existing)
                    while (bytes.hasRemaining()) local.write(bytes)
                  }
                  digest.update(buffer, 0, count)
                  position += count
                  progress(position)
                }
                local.force(true)
                unchanged()
                // A second read detects content replacement even when size and timestamps are
                // reused.
                if (!digest.digest().contentEquals(hash(remote, entry.sizeBytes, cancelled)))
                    throw DownloadFailure("REMOTE_CHANGED")
                unchanged()
              }
            }
          }
        }
        if (cancelled()) throw DownloadFailure("CANCELLED")
        if (staging != target) {
          if (Files.exists(target, NOFOLLOW_LINKS)) throw DownloadFailure("LOCAL_CONFLICT")
          Files.move(staging, target, ATOMIC_MOVE)
        }
        progress(entry.sizeBytes)
      }
    }
  }

  private fun hash(remote: RemoteFile, size: Long, cancelled: () -> Boolean): ByteArray {
    val digest = MessageDigest.getInstance("SHA-256")
    val buffer = ByteArray(COPY_BUFFER_BYTES)
    var position = 0L
    while (position < size) {
      if (cancelled()) throw DownloadFailure("CANCELLED")
      val count =
          remote.read(position, buffer, 0, minOf(buffer.size.toLong(), size - position).toInt())
      if (count <= 0) throw DownloadFailure("REMOTE_CHANGED")
      digest.update(buffer, 0, count)
      position += count
    }
    return digest.digest()
  }

  internal fun safeTarget(root: Path, relative: String, internal: Boolean = false): Path {
    val parts = relative.split('/')
    require(parts.all { it.isNotEmpty() && it != "." && it != ".." && '\u0000' !in it })
    require(internal || parts.first() != STAGING_DIRECTORY)
    var parent = root
    for (part in parts.dropLast(1)) {
      parent = parent.resolve(part)
      if (!Files.exists(parent, NOFOLLOW_LINKS)) Files.createDirectory(parent)
      require(Files.isDirectory(parent, NOFOLLOW_LINKS))
    }
    val target = parent.resolve(parts.last())
    require(!Files.exists(target, NOFOLLOW_LINKS) || Files.isRegularFile(target, NOFOLLOW_LINKS))
    return target
  }
}
