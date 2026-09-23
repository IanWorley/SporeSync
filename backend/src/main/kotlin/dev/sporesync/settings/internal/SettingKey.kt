package dev.sporesync.settings.internal

/** Declare each key once so its name, type, and string representation stay together. */
class SettingKey<T : Any>(
    val name: String,
    val parse: (String) -> T,
    val format: (T) -> String,
) {
  init {
    require(name.isNotBlank()) { "Setting name must not be blank" }
  }
}
