package com.sporesync.config

import org.springframework.util.unit.DataSize
import java.nio.file.Path
import java.time.Duration

private const val DEFAULT_SSH_PORT = 22
private const val DEFAULT_LOCAL_DIR = "/downloads"
private const val DEFAULT_REMOTE_SCANNER_PATH = "~/.sporesync"
private const val DEFAULT_CONCURRENT_FILE_LIMIT = 2
private const val DEFAULT_PER_FILE_CONNECTION_LIMIT = 4
private const val DEFAULT_REMOTE_SCAN_SECONDS = 30L
private const val DEFAULT_LOCAL_SCAN_SECONDS = 10L
private const val MASKED_SECRET = "****"

/** Defaults describe a fresh, unconfigured install. */
data class Settings(
    val general: GeneralSettings = GeneralSettings(),
    val connection: ConnectionSettings = ConnectionSettings(),
    val paths: PathSettings = PathSettings(),
    val transfer: TransferSettings = TransferSettings(),
    val scan: ScanSettings = ScanSettings(),
    val extraction: ExtractionSettings = ExtractionSettings(),
    val autoqueue: AutoQueueSettings = AutoQueueSettings(),
)

data class GeneralSettings(
    /** Turns on DEBUG logging for SporeSync's own code. */
    val debug: Boolean = false,
    val verbose: Boolean = false,
)

enum class AuthMethod { PASSWORD, KEY }

data class ConnectionSettings(
    val host: String = "",
    val port: Int = DEFAULT_SSH_PORT,
    val username: String = "",
    val authMethod: AuthMethod = AuthMethod.PASSWORD,
    /** Private key used when [authMethod] is [AuthMethod.KEY]. */
    val keyPath: Path? = null,
    /** Password used when [authMethod] is [AuthMethod.PASSWORD]. Masked in [toString] so it never reaches logs. */
    val password: String? = null,
) {
    val isConfigured: Boolean get() = host.isNotBlank()

    override fun toString(): String =
        "ConnectionSettings(host=$host, port=$port, username=$username, authMethod=$authMethod, " +
            "keyPath=$keyPath, password=${password?.let { MASKED_SECRET }})"
}

data class PathSettings(
    /** Directory on the remote host to mirror. */
    val remoteDir: String = "",
    val localDir: Path = Path.of(DEFAULT_LOCAL_DIR),
    /** Directory on the remote host the scanner script is copied into. */
    val remoteScannerPath: String = DEFAULT_REMOTE_SCANNER_PATH,
)

data class TransferSettings(
    val concurrentFileLimit: Int = DEFAULT_CONCURRENT_FILE_LIMIT,
    val perFileConnectionLimit: Int = DEFAULT_PER_FILE_CONNECTION_LIMIT,
    /** Total bytes per second across all transfers. `null` means unlimited. */
    val bandwidthLimit: DataSize? = null,
)

data class ScanSettings(
    val remoteInterval: Duration = Duration.ofSeconds(DEFAULT_REMOTE_SCAN_SECONDS),
    val localInterval: Duration = Duration.ofSeconds(DEFAULT_LOCAL_SCAN_SECONDS),
)

data class ExtractionSettings(
    val enabled: Boolean = true,
    val deleteArchiveAfterExtract: Boolean = false,
)

data class AutoQueueSettings(
    val enabled: Boolean = true,
    /** When on, only files matching a stored pattern are queued; when off, every new remote file is. */
    val patternsOnly: Boolean = false,
)
