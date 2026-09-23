package dev.sporesync.downloads.internal

import com.sun.jna.Library
import com.sun.jna.Native
import com.sun.jna.Platform
import java.nio.ByteBuffer
import java.nio.file.Files
import java.nio.file.Path
import java.security.MessageDigest
import java.util.UUID
import org.springframework.stereotype.Component

private const val STAGING_DIRECTORY = ".sporesync"
private const val OWNER_DIRECTORY_MODE = 448 // POSIX 0700
private const val OWNER_FILE_MODE = 384 // POSIX 0600
private const val CONTENT_DIRECTORY_MODE = 511 // POSIX 0777, reduced by umask
private const val CONTENT_FILE_MODE = 438 // POSIX 0666, reduced by umask
private const val ALREADY_EXISTS = 17 // POSIX EEXIST
private const val NOT_FOUND = 2 // POSIX ENOENT
private const val READ_WRITE = 2 // POSIX O_RDWR
private const val NO_FLAGS = 0
private const val SEEK_SET = 0
private const val SEEK_CURRENT = 1
private const val SEEK_END = 2
private const val LOCK_EXCLUSIVE = 2
private const val LOCK_NONBLOCKING = 4

internal interface LibC : Library {
  fun open(path: String, flags: Int, vararg mode: Any): Int

  fun openat(directory: Int, path: String, flags: Int, vararg mode: Any): Int

  fun mkdirat(directory: Int, path: String, mode: Int): Int

  fun linkat(source: Int, name: String, destination: Int, target: String, flags: Int): Int

  fun unlinkat(directory: Int, name: String, flags: Int): Int

  fun close(descriptor: Int): Int

  fun flock(descriptor: Int, operation: Int): Int

  fun geteuid(): Int

  fun read(descriptor: Int, bytes: ByteArray, length: Long): Long

  fun write(descriptor: Int, bytes: ByteArray, length: Long): Long

  fun lseek(descriptor: Int, offset: Long, origin: Int): Long

  fun fsync(descriptor: Int): Int

  fun fchmod(descriptor: Int, mode: Int): Int
}

/**
 * Holds directory descriptors throughout the transfer; never reopens a validated parent by path.
 */
@Component
class PosixDownloadStorage internal constructor(private val api: Posix) : DownloadStorage {
  constructor() : this(Posix())

  override fun open(destination: String, relative: String, temporary: Boolean): DownloadTarget {
    val parts = pathParts(relative)
    val owned = mutableListOf<AutoCloseable>()
    fun <T : AutoCloseable> own(value: T): T = value.also { owned.add(it) }
    try {
      // The configured root may be an explicit alias. Descendants are always opened without links.
      val root = own(api.root(Path.of(destination)))
      val staging = own(root.directory(STAGING_DIRECTORY, OWNER_DIRECTORY_MODE))
      staging.requirePrivate()
      own(staging.lock())
      var parent = root
      for (part in parts.dropLast(1)) parent = own(parent.directory(part))
      val name = parts.last()
      val existing = parent.file(name, create = false)
      val staged = temporary && existing == null
      val key = stagingKey(relative)
      val file = own(existing ?: if (staged) staging.file(key)!! else parent.file(name)!!)
      if (staged) staging.probeLink(parent)
      return object : DownloadTarget {
        override val file = file

        override fun publish() {
          if (staged) {
            staging.link(key, parent, name)
            parent.sync()
            staging.unlink(key)
            staging.sync()
          } else parent.sync()
        }

        override fun close() {
          owned.asReversed().forEach { it.close() }
        }
      }
    } catch (error: Throwable) {
      owned.asReversed().forEach { resource ->
        try {
          resource.close()
        } catch (cleanup: Throwable) {
          error.addSuppressed(cleanup)
        }
      }
      throw error
    }
  }

  override fun missing(destination: String, relative: String): Boolean =
      inspect(destination, relative) { parent, name, _ ->
        if (parent == null) true else parent.file(name, create = false)?.use { false } ?: true
      }

  override fun delete(destination: String, relative: String) {
    inspect(destination, relative) { parent, name, staging ->
      parent?.removeFile(name)
      staging.removeFile(stagingKey(relative))
    }
  }

  private fun <T> inspect(
      destination: String,
      relative: String,
      operation: (Posix.Directory?, String, Posix.Directory) -> T,
  ): T {
    val parts = pathParts(relative)
    val owned = mutableListOf<AutoCloseable>()
    fun <R : AutoCloseable> own(value: R): R = value.also { owned.add(it) }
    try {
      val root = own(api.root(Path.of(destination)))
      val staging = own(root.directory(STAGING_DIRECTORY, OWNER_DIRECTORY_MODE))
      staging.requirePrivate()
      own(staging.lock())
      var parent: Posix.Directory? = root
      for (part in parts.dropLast(1)) parent = parent?.existingDirectory(part)?.let(::own)
      return operation(parent, parts.last(), staging)
    } finally {
      owned.asReversed().forEach { it.close() }
    }
  }

  private fun pathParts(relative: String): List<String> =
      relative.split('/').also { parts ->
        require(parts.all { it.isNotEmpty() && it != "." && it != ".." && '\u0000' !in it })
        require(parts.first() != STAGING_DIRECTORY)
      }

  private fun stagingKey(relative: String) =
      MessageDigest.getInstance("SHA-256").digest(relative.toByteArray()).toHexString() + ".part"
}

internal class Posix(val libc: LibC = Native.load(Platform.C_LIBRARY_NAME, LibC::class.java)) {
  init {
    require(Platform.isLinux() || Platform.isMac()) { "UNSUPPORTED_DESTINATION_PLATFORM" }
  }

  // Values from Linux fcntl.h and Darwin sys/fcntl.h, respectively.
  private val directoryFlag = if (Platform.isMac()) 0x100000 else 0x10000
  private val noFollow = if (Platform.isMac()) 0x100 else 0x20000
  private val closeOnExec = if (Platform.isMac()) 0x1000000 else 0x80000
  private val create = if (Platform.isMac()) 0x200 else 0x40
  private val exclusive = if (Platform.isMac()) 0x800 else 0x80
  private val nonBlocking = if (Platform.isMac()) 0x4 else 0x800
  private val directoryFlags = directoryFlag or noFollow or closeOnExec

  fun root(path: Path): Directory {
    require(path.isAbsolute)
    // Walk from / so missing configured directories are also created descriptor-relatively.
    val canonical = path.toAbsolutePath().normalize()
    var anchor = canonical
    val missing = mutableListOf<Path>()
    while (!Files.exists(anchor, java.nio.file.LinkOption.NOFOLLOW_LINKS)) {
      missing.add(anchor.fileName)
      anchor = requireNotNull(anchor.parent)
    }
    val resolved =
        missing.asReversed().fold(anchor.toRealPath()) { parent, child -> parent.resolve(child) }
    var current = Directory(check(libc.open("/", directoryFlags, NO_FLAGS)))
    try {
      for (part in resolved) {
        val next = current.directory(part.toString())
        current.close()
        current = next
      }
      return current
    } catch (error: Throwable) {
      current.close()
      throw error
    }
  }

  private fun descriptorPath(fd: Int) =
      Path.of(if (Platform.isMac()) "/dev/fd/$fd" else "/proc/self/fd/$fd")

  private fun check(result: Int): Int {
    if (result < 0) throw DownloadFailure("UNSAFE_DESTINATION")
    return result
  }

  inner class NativeFile(private val fd: Int) : DownloadFile {
    private var opened = true

    private fun checked(result: Long): Long {
      if (result < 0) throw DownloadFailure("LOCAL_IO_FAILED")
      return result
    }

    override fun isOpen() = opened

    override fun read(destination: ByteBuffer): Int {
      check(opened)
      if (!destination.hasRemaining()) return 0
      val bytes = ByteArray(destination.remaining())
      val count = checked(libc.read(fd, bytes, bytes.size.toLong())).toInt()
      if (count == 0) return -1
      destination.put(bytes, 0, count)
      return count
    }

    override fun write(source: ByteBuffer): Int {
      check(opened)
      val bytes = ByteArray(source.remaining())
      source.duplicate().get(bytes)
      val count = checked(libc.write(fd, bytes, bytes.size.toLong())).toInt()
      source.position(source.position() + count)
      return count
    }

    override fun position(): Long {
      check(opened)
      return checked(libc.lseek(fd, 0, SEEK_CURRENT))
    }

    override fun position(newPosition: Long): DownloadFile {
      check(opened)
      require(newPosition >= 0)
      checked(libc.lseek(fd, newPosition, SEEK_SET))
      return this
    }

    override fun size(): Long {
      val previous = position()
      val size = checked(libc.lseek(fd, 0, SEEK_END))
      position(previous)
      return size
    }

    override fun truncate(size: Long): DownloadFile =
        throw UnsupportedOperationException("Downloads never truncate existing bytes")

    override fun force() {
      check(opened)
      checked(libc.fsync(fd).toLong())
    }

    override fun close() {
      if (opened) {
        opened = false
        libc.close(fd)
      }
    }
  }

  inner class Directory(private val descriptor: Int) : AutoCloseable {
    fun directory(name: String, mode: Int = CONTENT_DIRECTORY_MODE): Directory {
      var child = libc.openat(descriptor, name, directoryFlags, NO_FLAGS)
      if (child < 0 && Native.getLastError() == NOT_FOUND) {
        val created = libc.mkdirat(descriptor, name, mode)
        if (created < 0 && Native.getLastError() != ALREADY_EXISTS)
            throw DownloadFailure("UNSAFE_DESTINATION")
        if (created == 0) sync()
        child = libc.openat(descriptor, name, directoryFlags, NO_FLAGS)
      }
      return Directory(check(child))
    }

    fun existingDirectory(name: String): Directory? {
      val child = libc.openat(descriptor, name, directoryFlags, NO_FLAGS)
      if (child < 0 && Native.getLastError() == NOT_FOUND) return null
      return Directory(check(child))
    }

    fun removeFile(name: String) {
      file(name, create = false)?.use {
        if (libc.unlinkat(descriptor, name, NO_FLAGS) < 0 && Native.getLastError() != NOT_FOUND)
            throw DownloadFailure("LOCAL_IO_FAILED")
        sync()
      }
    }

    fun requirePrivate() {
      val handle = descriptorPath(descriptor)
      require(Files.getAttribute(handle, "unix:uid") == libc.geteuid()) {
        "UNSAFE_STAGING_DIRECTORY"
      }
      // Upgrade staging directories created by earlier releases without trusting their path again.
      check(libc.fchmod(descriptor, OWNER_DIRECTORY_MODE))
    }

    fun lock(): AutoCloseable {
      val fd =
          check(
              libc.openat(
                  descriptor,
                  "worker.lock",
                  READ_WRITE or noFollow or closeOnExec or create or nonBlocking,
                  OWNER_FILE_MODE,
              )
          )
      try {
        require(Files.isRegularFile(descriptorPath(fd))) { "UNSAFE_DESTINATION" }
        if (libc.flock(fd, LOCK_EXCLUSIVE or LOCK_NONBLOCKING) < 0)
            throw DownloadFailure("DESTINATION_BUSY")
        return AutoCloseable { libc.close(fd) }
      } catch (error: Throwable) {
        libc.close(fd)
        throw error
      }
    }

    fun file(name: String, create: Boolean = true, exclusive: Boolean = false): DownloadFile? {
      val flags =
          READ_WRITE or
              noFollow or
              closeOnExec or
              nonBlocking or
              (if (create) this@Posix.create else NO_FLAGS) or
              (if (exclusive) this@Posix.exclusive else NO_FLAGS)
      val fd = libc.openat(descriptor, name, flags, CONTENT_FILE_MODE)
      if (fd < 0 && !create && Native.getLastError() == NOT_FOUND) return null
      check(fd)
      try {
        // These kernel descriptor paths refer to the held inode, not to its mutable original name.
        val handle = descriptorPath(fd)
        require(Files.isRegularFile(handle)) { "UNSAFE_DESTINATION" }
        if (create) sync()
        return NativeFile(fd)
      } catch (error: Throwable) {
        libc.close(fd)
        throw error
      }
    }

    fun link(name: String, target: Directory, destination: String) {
      if (libc.linkat(descriptor, name, target.descriptor, destination, NO_FLAGS) < 0) {
        throw DownloadFailure(
            if (Native.getLastError() == ALREADY_EXISTS) "LOCAL_CONFLICT"
            else "UNSUPPORTED_DESTINATION"
        )
      }
    }

    fun sync() {
      if (libc.fsync(descriptor) < 0) throw DownloadFailure("LOCAL_IO_FAILED")
    }

    fun unlink(name: String) {
      check(libc.unlinkat(descriptor, name, NO_FLAGS))
    }

    fun probeLink(target: Directory) {
      val probe = ".sporesync-probe-${UUID.randomUUID()}"
      file(probe, exclusive = true)!!.close()
      var linked = false
      try {
        link(probe, target, probe)
        linked = true
      } finally {
        if (linked) target.unlink(probe)
        unlink(probe)
      }
    }

    override fun close() {
      libc.close(descriptor)
    }
  }
}
