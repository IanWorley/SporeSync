package com.sporesync.remotescan

import java.nio.file.Path

private const val DEFAULT_SSH_PORT = 22
private const val MIN_SSH_PORT = 1
private const val MAX_SSH_PORT = 65535
private const val MASKED_SECRET = "****"

enum class SshAuthMethod { PASSWORD, KEY }

data class SshConnectionConfig(
    val host: String,
    val port: Int = DEFAULT_SSH_PORT,
    val username: String,
    val authMethod: SshAuthMethod,
    val password: String? = null,
    val keyPath: Path? = null,
    val knownHostsFile: Path? = null,
    val hostKeyFingerprint: String? = null,
) {
    init {
        require(host.isNotBlank()) { "host must not be blank" }
        require(port in MIN_SSH_PORT..MAX_SSH_PORT) { "port must be between $MIN_SSH_PORT and $MAX_SSH_PORT" }
        require(username.isNotBlank()) { "username must not be blank" }
        when (authMethod) {
            SshAuthMethod.PASSWORD -> require(!password.isNullOrBlank()) { "password auth requires a password" }
            SshAuthMethod.KEY -> require(keyPath != null) { "key auth requires keyPath" }
        }
        require(knownHostsFile != null || !hostKeyFingerprint.isNullOrBlank()) {
            "knownHostsFile or hostKeyFingerprint must be set"
        }
    }

    override fun toString(): String =
        "SshConnectionConfig(" +
            "host=$host, " +
            "port=$port, " +
            "username=$username, " +
            "authMethod=$authMethod, " +
            "password=${password?.let { MASKED_SECRET }}, " +
            "keyPath=$keyPath, " +
            "knownHostsFile=$knownHostsFile, " +
            "hostKeyFingerprint=$hostKeyFingerprint)"
}
