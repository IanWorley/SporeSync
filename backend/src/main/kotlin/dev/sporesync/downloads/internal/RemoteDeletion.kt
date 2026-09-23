package dev.sporesync.downloads.internal

import dev.sporesync.downloads.DownloadSpec
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
    import errno, fcntl, hashlib, os, stat, sys
    root, path, size, mtime = sys.argv[1:]
    size, mtime = int(size), int(mtime)
    parts = path.split('/')
    if any(p in ('', '.', '..') for p in parts) or parts[0] == '.sporesync': sys.exit($REMOTE_CHANGED_EXIT)
    directory_flags = os.O_RDONLY | os.O_DIRECTORY | os.O_NOFOLLOW
    owned = []
    def own(fd):
        owned.append(fd)
        return fd
    def private_directory(parent, name):
        try: os.mkdir(name, 0o700, dir_fd=parent)
        except FileExistsError: pass
        fd = own(os.open(name, directory_flags, dir_fd=parent))
        metadata = os.fstat(fd)
        if metadata.st_uid != os.geteuid() or metadata.st_mode & 0o077: raise PermissionError()
        os.fsync(parent)
        return fd
    def source_parent():
        parent = root_fd
        for part in parts[:-1]: parent = own(os.open(part, directory_flags, dir_fd=parent))
        return parent
    def matches(metadata):
        return stat.S_ISREG(metadata.st_mode) and metadata.st_size == size and metadata.st_mtime_ns == mtime
    def restore():
        try:
            parent = source_parent()
            os.link('entry', parts[-1], src_dir_fd=quarantine, dst_dir_fd=parent, follow_symlinks=False)
            os.fsync(parent)
            os.unlink('entry', dir_fd=quarantine)
            os.fsync(quarantine)
        except OSError:
            pass
    try:
        root_fd = own(os.open(root, os.O_RDONLY | os.O_DIRECTORY))
        staging = private_directory(root_fd, '.sporesync')
        key = hashlib.sha256(('\0'.join((path, str(size), str(mtime)))).encode()).hexdigest()
        quarantine = private_directory(staging, 'delete-' + key)
        fcntl.flock(quarantine, fcntl.LOCK_EX | fcntl.LOCK_NB)
        before = None
        try:
            os.stat('entry', dir_fd=quarantine, follow_symlinks=False)
        except FileNotFoundError:
            parent = source_parent()
            before = os.stat(parts[-1], dir_fd=parent, follow_symlinks=False)
            if not matches(before): sys.exit($REMOTE_CHANGED_EXIT)
            os.rename(parts[-1], 'entry', src_dir_fd=parent, dst_dir_fd=quarantine)
            os.fsync(quarantine)
            os.fsync(parent)
        try:
            captured = own(os.open('entry', os.O_RDONLY | os.O_NOFOLLOW | os.O_NONBLOCK, dir_fd=quarantine))
            metadata = os.fstat(captured)
        except OSError:
            restore()
            raise
        if not matches(metadata) or (before is not None and (before.st_dev, before.st_ino) != (metadata.st_dev, metadata.st_ino)):
            restore()
            sys.exit($REMOTE_CHANGED_EXIT)
        os.unlink('entry', dir_fd=quarantine)
        os.fsync(quarantine)
    except FileNotFoundError:
        pass
    except PermissionError:
        sys.exit($REMOTE_DELETE_DENIED_EXIT)
    except OSError as error:
        if error.errno in (errno.ELOOP, errno.ENOTDIR): sys.exit($REMOTE_CHANGED_EXIT)
        sys.exit($REMOTE_DELETE_FAILED_EXIT)
    finally:
        for fd in reversed(owned): os.close(fd)
    """
        .trimIndent()
