package dev.sporesync.config

import dev.sporesync.model.settings.ApplicationSettings
import dev.sporesync.model.settings.SettingKey
import dev.sporesync.model.settings.SettingNames

object SshSettingKeys {
  val HOST = SettingKey(SettingNames.SSH_HOST, { it }, { value: String -> value })
  val PORT = SettingKey(SettingNames.SSH_PORT, String::toInt, Int::toString)
  val USERNAME = SettingKey(SettingNames.SSH_USERNAME, { it }, { value: String -> value })
  val SOURCE = SettingKey(SettingNames.REMOTE_SOURCE_DIRECTORY, { it }, { value: String -> value })
  val TIMEOUT_MILLIS = SettingKey(SettingNames.SSH_TIMEOUT_MILLIS, String::toInt, Int::toString)
}

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
    require('\u0000' !in source && port in MIN_PORT..MAX_PORT && timeoutMillis > 0)
  }

  companion object {
    private const val MIN_PORT = 1
    private const val MAX_PORT = 65535

    fun load(settings: ApplicationSettings): SshConnectionSettings {
      fun <T : Any> required(key: SettingKey<T>): T =
          requireNotNull(settings.get(key)) { "Missing application setting: ${key.name}" }
      return SshConnectionSettings(
          required(SshSettingKeys.HOST),
          required(SshSettingKeys.PORT),
          required(SshSettingKeys.USERNAME),
          required(SshSettingKeys.SOURCE),
          required(SshSettingKeys.TIMEOUT_MILLIS),
      )
    }
  }
}
