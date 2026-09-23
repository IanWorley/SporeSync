package dev.sporesync.downloads.internal

import dev.sporesync.downloads.DownloadSpec
import java.util.concurrent.Executors
import java.util.concurrent.TimeUnit
import java.util.concurrent.TimeoutException
import net.schmizz.sshj.SSHClient

private const val SHA256_HEX_LENGTH = 64
private const val CHECKSUM_OUTPUT_LIMIT = 2 * SHA256_HEX_LENGTH + 2
private const val REMOTE_CHANGED_EXIT = 2

internal data class RemoteChecksum(val prefix: String, val complete: String) {
  companion object {
    fun read(
        ssh: SSHClient,
        spec: DownloadSpec,
        prefixBytes: Long,
        cancelled: () -> Boolean,
    ): RemoteChecksum {
      val arguments =
          listOf(
              spec.settings.source,
              spec.entry.path,
              spec.entry.sizeBytes.toString(),
              spec.entry.modifiedTimeNs.toString(),
              prefixBytes.toString(),
          )
      val command =
          "python3 -c ${quote(CHECKSUM_SCRIPT)} " + arguments.joinToString(" ", transform = ::quote)
      ssh.startSession().use { session ->
        session.exec(command).use { process ->
          val readers = Executors.newVirtualThreadPerTaskExecutor()
          try {
            fun readByte(): Int {
              if (cancelled()) throw DownloadFailure("CANCELLED")
              return readers
                  .submit<Int> { process.inputStream.read() }
                  .get(spec.settings.timeoutMillis.toLong(), TimeUnit.MILLISECONDS)
            }
            var first = readByte()
            while (first == '.'.code) first = readByte()
            val output = StringBuilder()
            while (first >= 0 && first != '\n'.code && output.length <= CHECKSUM_OUTPUT_LIMIT) {
              output.append(first.toChar())
              first = readByte()
            }
            process.join(spec.settings.timeoutMillis.toLong(), TimeUnit.MILLISECONDS)
            if (process.exitStatus == null) {
              ssh.disconnect()
              throw DownloadFailure("REMOTE_HASH_TIMEOUT")
            }
            if (process.exitStatus == REMOTE_CHANGED_EXIT) throw DownloadFailure("REMOTE_CHANGED")
            if (process.exitStatus != 0) throw DownloadFailure("REMOTE_HASH_FAILED")
            val hashes = output.toString().split(' ')
            if (
                hashes.size != 2 ||
                    hashes.any { !it.matches(Regex("[0-9a-f]{$SHA256_HEX_LENGTH}")) }
            )
                throw DownloadFailure("REMOTE_HASH_FAILED")
            return RemoteChecksum(hashes[0], hashes[1])
          } catch (error: DownloadFailure) {
            ssh.disconnect()
            throw error
          } catch (_: TimeoutException) {
            ssh.disconnect()
            throw DownloadFailure("REMOTE_HASH_TIMEOUT")
          } catch (_: Exception) {
            ssh.disconnect()
            throw DownloadFailure("REMOTE_HASH_FAILED")
          } finally {
            readers.shutdownNow()
          }
        }
      }
    }

    private fun quote(value: String) = "'${value.replace("'", "'\"'\"'")}'"
  }
}

// Python is already required on the seedbox. Only fixed-size hashes cross the network.
private val CHECKSUM_SCRIPT =
    """
    import hashlib, os, stat, sys, time
    root, path, size, mtime, prefix = sys.argv[1:]
    size, mtime, prefix = int(size), int(mtime), int(prefix)
    block_size = 1024 * 1024
    heartbeat_seconds = 0.1
    fd = os.open(root, os.O_RDONLY | os.O_DIRECTORY)
    try:
        parts = path.split('/')
        if any(p in ('', '.', '..') for p in parts): sys.exit(2)
        for part in parts[:-1]:
            child = os.open(part, os.O_RDONLY | os.O_DIRECTORY | os.O_NOFOLLOW, dir_fd=fd)
            os.close(fd)
            fd = child
        source = os.open(parts[-1], os.O_RDONLY | os.O_NOFOLLOW | os.O_NONBLOCK, dir_fd=fd)
        with os.fdopen(source, 'rb') as stream:
            before = os.fstat(stream.fileno())
            if not stat.S_ISREG(before.st_mode): sys.exit(2)
            if before.st_size != size or before.st_mtime_ns != mtime: sys.exit(2)
            full, head, position = hashlib.sha256(), hashlib.sha256(), 0
            heartbeat = time.monotonic()
            print('.', end='', flush=True)
            while position < size:
                chunk = stream.read(min(block_size, size - position))
                if not chunk: sys.exit(2)
                full.update(chunk)
                head.update(chunk[:max(0, prefix - position)])
                position += len(chunk)
                if time.monotonic() - heartbeat >= heartbeat_seconds:
                    print('.', end='', flush=True)
                    heartbeat = time.monotonic()
            after = os.fstat(stream.fileno())
            current = os.stat(parts[-1], dir_fd=fd, follow_symlinks=False)
            identity = lambda s: (s.st_dev, s.st_ino, s.st_size, s.st_mtime_ns, s.st_ctime_ns)
            if identity(before) != identity(after) or identity(after) != identity(current): sys.exit(2)
            print(head.hexdigest(), full.hexdigest())
    finally:
        os.close(fd)
    """
        .trimIndent()
