package dev.sporesync.model.download

import java.util.concurrent.TimeUnit
import net.schmizz.sshj.SSHClient

private const val REMOTE_DELETE_FAILED_EXIT = 1
private const val REMOTE_CHANGED_EXIT = 2
private const val REMOTE_DELETE_DENIED_EXIT = 3

internal object RemoteDeletion {
  fun delete(ssh: SSHClient, spec: DownloadSpec) {
    val arguments =
        listOf(
            spec.settings.source,
            spec.entry.path,
            spec.entry.sizeBytes.toString(),
            spec.entry.modifiedTimeNs.toString(),
        )
    fun quote(value: String) = "'${value.replace("'", "'\"'\"'")}'"
    val command =
        "python3 -c ${quote(DELETE_SCRIPT)} " + arguments.joinToString(" ", transform = ::quote)
    ssh.startSession().use { session ->
      session.exec(command).use { process ->
        process.join(spec.settings.timeoutMillis.toLong(), TimeUnit.MILLISECONDS)
        when (process.exitStatus) {
          0 -> Unit
          REMOTE_CHANGED_EXIT -> throw DownloadFailure("REMOTE_CHANGED")
          REMOTE_DELETE_DENIED_EXIT -> throw DownloadFailure("REMOTE_DELETE_DENIED")
          null -> {
            ssh.disconnect()
            throw DownloadFailure("REMOTE_DELETE_TIMEOUT")
          }
          else -> throw DownloadFailure("REMOTE_DELETE_FAILED")
        }
      }
    }
  }
}

private val DELETE_SCRIPT =
    """
    import errno, os, stat, sys
    root, path, size, mtime = sys.argv[1:]
    size, mtime = int(size), int(mtime)
    parts = path.split('/')
    if any(p in ('', '.', '..') for p in parts): sys.exit($REMOTE_CHANGED_EXIT)
    fd = None
    try:
        fd = os.open(root, os.O_RDONLY | os.O_DIRECTORY)
        for part in parts[:-1]:
            child = os.open(part, os.O_RDONLY | os.O_DIRECTORY | os.O_NOFOLLOW, dir_fd=fd)
            os.close(fd)
            fd = child
        before = os.stat(parts[-1], dir_fd=fd, follow_symlinks=False)
        if not stat.S_ISREG(before.st_mode): sys.exit($REMOTE_CHANGED_EXIT)
        if before.st_size != size or before.st_mtime_ns != mtime: sys.exit($REMOTE_CHANGED_EXIT)
        os.unlink(parts[-1], dir_fd=fd)
        os.fsync(fd)
    except FileNotFoundError:
        pass
    except PermissionError:
        sys.exit($REMOTE_DELETE_DENIED_EXIT)
    except OSError as error:
        if error.errno in (errno.ELOOP, errno.ENOTDIR): sys.exit($REMOTE_CHANGED_EXIT)
        sys.exit($REMOTE_DELETE_FAILED_EXIT)
    finally:
        if fd is not None: os.close(fd)
    """
        .trimIndent()
