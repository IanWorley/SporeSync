package dev.sporesync.settings.internal

object SshSettingKeys {
  val HOST = SettingKey(SettingNames.SSH_HOST, { it }, { value: String -> value })
  val PORT = SettingKey(SettingNames.SSH_PORT, String::toInt, Int::toString)
  val USERNAME = SettingKey(SettingNames.SSH_USERNAME, { it }, { value: String -> value })
  val SOURCE = SettingKey(SettingNames.REMOTE_SOURCE_DIRECTORY, { it }, { value: String -> value })
  val TIMEOUT_MILLIS = SettingKey(SettingNames.SSH_TIMEOUT_MILLIS, String::toInt, Int::toString)
}
