package com.sporesync.remotescan

import java.nio.file.Path

private const val DEFAULT_SSH_PORT = 22
private const val MASKED_SECRET = "****"

enum class SshAuthMethod { PASSWORD, KEY }

data class SshConnectionConfig(
    val host: String,
    val port: Int = DEFAULT_SSH_PORT,
    val username: String,
    val authMethod: SshAuthMethod,
    val password: String? = null,
    val keyPath: Path? = null,
) {
    init {
        require(host.isNotBlank()) { "host must not be blank" }
        require(port in 1..65535) { "port must be between 1 and 65535" }
        require(username.isNotBlank()) { "username must not be blank" }
        when (authMethod) {
            SshAuthMethod.PASSWORD -> require(!password.isNullOrBlank()) { "password auth requires a password" }
            SshAuthMethod.KEY -> require(keyPath != null) { "key auth requires keyPath" }
        }
    }

    override fun toString(): String =
        "SshConnectionConfig(" +
            "host=$host, " +
            "port=$port, " +
            "username=$username, " +
            "authMethod=$authMethod, " +
            "password=${password?.let { MASKED_SECRET }}, " +
            "keyPath=$keyPath)"
}
