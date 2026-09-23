package dev.sporesync.settings

import java.nio.file.Path

const val DEFAULT_SCAN_SECONDS = 300L
const val MIN_SCAN_SECONDS = 10L
const val MAX_SCAN_SECONDS = 86400L
const val MIN_SSH_PORT = 1
const val MAX_SSH_PORT = 65535
const val DEFAULT_SSH_PORT = 22
const val DEFAULT_TIMEOUT_MILLIS = 30000
const val MAX_TIMEOUT_MILLIS = 300000

/** Credentials and trust material deliberately have no representation in the browser contract. */
data class DownloadSettings(
    val host: String = "",
    val port: Int = DEFAULT_SSH_PORT,
    val username: String = "",
    val source: String = "",
    val destination: String = "",
    val scanSeconds: Long = DEFAULT_SCAN_SECONDS,
    val automatic: Boolean = true,
    val temporaryFiles: Boolean = true,
    val timeoutMillis: Int = DEFAULT_TIMEOUT_MILLIS,
) {
  fun connection(): SshConnectionSettings =
      SshConnectionSettings(host, port, username, source, timeoutMillis)

  fun validate() {
    validateDiscovery()
    require(destination.isNotBlank() && Path.of(destination).isAbsolute)
  }

  fun validateDiscovery() {
    require(host.isNotBlank() && username.isNotBlank())
    require(port in MIN_SSH_PORT..MAX_SSH_PORT)
    require(timeoutMillis in 1..MAX_TIMEOUT_MILLIS)
    require(source.startsWith('/') && '\u0000' !in source)
    require(scanSeconds in MIN_SCAN_SECONDS..MAX_SCAN_SECONDS)
  }
}
