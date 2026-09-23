package dev.sporesync.settings

/** Read once per scan so edits apply to the next connection, not an in-flight command. */
data class SshConnectionSettings(
    val host: String,
    val port: Int,
    val username: String,
    val source: String,
    val timeoutMillis: Int,
) {
  init {
    require(host.isNotBlank() && username.isNotBlank() && source.startsWith("/"))
    require('\u0000' !in source && port in MIN_SSH_PORT..MAX_SSH_PORT && timeoutMillis > 0)
  }
}
