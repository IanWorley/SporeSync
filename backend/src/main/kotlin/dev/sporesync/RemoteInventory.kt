package dev.sporesync

import dev.sporesync.settings.ApplicationSettings
import java.io.InputStream
import java.nio.file.Files
import java.nio.file.Path
import java.security.MessageDigest
import java.util.UUID
import java.util.concurrent.Executors
import java.util.concurrent.TimeUnit
import java.util.concurrent.TimeoutException
import net.schmizz.sshj.SSHClient
import net.schmizz.sshj.sftp.SFTPClient
import net.schmizz.sshj.xfer.InMemorySourceFile
import org.springframework.boot.context.properties.ConfigurationProperties
import org.springframework.http.HttpStatus
import org.springframework.stereotype.Component
import org.springframework.stereotype.Service
import org.springframework.web.bind.annotation.ExceptionHandler
import org.springframework.web.bind.annotation.PostMapping
import org.springframework.web.bind.annotation.ResponseStatus
import org.springframework.web.bind.annotation.RestController
import tools.jackson.databind.DeserializationFeature
import tools.jackson.databind.json.JsonMapper

private const val INVENTORY_VERSION = 1
private const val MAX_INVENTORY_BYTES = 16 * 1024 * 1024
private const val MAX_ERROR_BYTES = 64 * 1024
private const val PRIVATE_DIRECTORY_MODE = 448 // POSIX 0700
private const val SUCCESS_EXIT = 0

// Deliberately not a data class: generated toString must not expose credentials.
@Component
@ConfigurationProperties("sporesync.ssh")
class SshSettings {
  var privateKey: String = ""
  var passphrase: String = ""
  var knownHosts: String = ""
  var scanner: String = "../scanner/inventory.py"

  fun validate() {
    require(
        listOf(privateKey, knownHosts, scanner).all {
          it.isNotBlank() && Files.isRegularFile(Path.of(it))
        }
    )
  }
}

enum class EntryType {
  file,
  directory,
  symlink,
  other,
}

data class InventoryEntry(
    val path: String,
    val type: EntryType,
    val sizeBytes: Long,
    val modifiedTimeNs: Long,
)

data class Inventory(val schemaVersion: Int, val entries: List<InventoryEntry>)

enum class InventoryFailure {
  CONFIGURATION,
  CONNECTION,
  AUTHENTICATION,
  UPLOAD,
  EXECUTION,
  TIMEOUT,
  PROTOCOL,
}

class InventoryException(val code: InventoryFailure) :
    RuntimeException("Remote inventory failed: $code")

@Service
class RemoteInventory(
    private val settings: SshSettings,
    private val applicationSettings: ApplicationSettings,
    private val mapper: JsonMapper,
) {
  @Synchronized
  fun scan(connectionOverride: SshConnectionSettings? = null): Inventory {
    var stage = InventoryFailure.CONFIGURATION
    try {
      settings.validate()
      val connection = connectionOverride ?: SshConnectionSettings.load(applicationSettings)
      val scanner = Files.readAllBytes(Path.of(settings.scanner))
      SSHClient().use { ssh ->
        ssh.connectTimeout = connection.timeoutMillis
        ssh.timeout = connection.timeoutMillis
        ssh.transport.timeoutMs = connection.timeoutMillis
        ssh.connection.timeoutMs = connection.timeoutMillis
        ssh.loadKnownHosts(Path.of(settings.knownHosts).toFile())
        val key = ssh.loadKeys(settings.privateKey, settings.passphrase)
        stage = InventoryFailure.CONNECTION
        ssh.connect(connection.host, connection.port)
        stage = InventoryFailure.AUTHENTICATION
        ssh.authPublickey(connection.username, key)
        stage = InventoryFailure.UPLOAD
        val remoteScanner = ssh.newSFTPClient().use { upload(it, scanner) }
        stage = InventoryFailure.EXECUTION
        val json =
            execute(
                ssh,
                "python3 ${quote(remoteScanner)} ${quote(connection.source)}",
                connection.timeoutMillis,
            )
        stage = InventoryFailure.PROTOCOL
        return decode(json)
      }
    } catch (error: InventoryException) {
      throw error
    } catch (_: Exception) {
      // SSH and remote stderr can contain sensitive paths; expose only a stable category.
      throw InventoryException(stage)
    }
  }

  internal fun decode(json: String): Inventory {
    try {
      val inventory =
          mapper
              .readerFor(Inventory::class.java)
              .with(DeserializationFeature.FAIL_ON_MISSING_CREATOR_PROPERTIES)
              .with(DeserializationFeature.FAIL_ON_NULL_FOR_PRIMITIVES)
              .with(DeserializationFeature.FAIL_ON_TRAILING_TOKENS)
              .readValue<Inventory>(json)
      require(inventory.schemaVersion == INVENTORY_VERSION)
      val paths = inventory.entries.map { it.path }
      require(paths.distinct().size == paths.size)
      require(
          inventory.entries.all {
            it.sizeBytes >= 0 &&
                it.path.isNotEmpty() &&
                '\u0000' !in it.path &&
                it.path.split('/').none { part -> part.isEmpty() || part == "." || part == ".." }
          }
      )
      return inventory
    } catch (_: Exception) {
      throw InventoryException(InventoryFailure.PROTOCOL)
    }
  }

  private fun upload(sftp: SFTPClient, bytes: ByteArray): String {
    val directory = "${sftp.canonicalize(".")}/.sporesync"
    if (sftp.statExistence(directory) == null) {
      sftp.mkdir(directory)
      sftp.chmod(directory, PRIVATE_DIRECTORY_MODE)
    }
    val hash = MessageDigest.getInstance("SHA-256").digest(bytes).toHexString()
    val target = "$directory/inventory-$hash.py"
    if (sftp.statExistence(target) == null) {
      val temporary = "$target.${UUID.randomUUID()}.tmp"
      try {
        sftp.put(
            object : InMemorySourceFile() {
              override fun getName() = "inventory.py"

              override fun getLength() = bytes.size.toLong()

              override fun getInputStream() = bytes.inputStream()
            },
            temporary,
        )
        sftp.rename(temporary, target)
      } finally {
        if (sftp.statExistence(temporary) != null) sftp.rm(temporary)
      }
    }
    return target
  }

  private fun execute(ssh: SSHClient, command: String, timeoutMillis: Int): String {
    val readers = Executors.newVirtualThreadPerTaskExecutor()
    try {
      ssh.startSession().use { session ->
        session.exec(command).use { process ->
          val output =
              readers.submit<String> { readLimited(process.inputStream, MAX_INVENTORY_BYTES) }
          val errors = readers.submit<String> { readLimited(process.errorStream, MAX_ERROR_BYTES) }
          try {
            process.join(timeoutMillis.toLong(), TimeUnit.MILLISECONDS)
          } catch (error: Exception) {
            // Disconnect before channel cleanup so a hung command cannot prolong the timeout.
            ssh.disconnect()
            if (error.cause is TimeoutException) throw InventoryException(InventoryFailure.TIMEOUT)
            throw error
          }
          require(process.exitStatus == SUCCESS_EXIT)
          errors.get(timeoutMillis.toLong(), TimeUnit.MILLISECONDS)
          return output.get(timeoutMillis.toLong(), TimeUnit.MILLISECONDS)
        }
      }
    } finally {
      readers.shutdownNow()
    }
  }

  private fun readLimited(stream: InputStream, limit: Int): String {
    val bytes = stream.readNBytes(limit + 1)
    require(bytes.size <= limit)
    return bytes.toString(Charsets.UTF_8)
  }

  private fun quote(value: String) = "'${value.replace("'", "'\"'\"'")}'"
}

@RestController
class InventoryController(private val inventory: RemoteInventory) {
  @PostMapping("/api/inventory/scan") fun scan(): Inventory = inventory.scan()

  @ExceptionHandler(InventoryException::class)
  @ResponseStatus(HttpStatus.BAD_GATEWAY)
  fun failed(error: InventoryException): Map<String, InventoryFailure> =
      mapOf("error" to error.code)
}
