package dev.sporesync.settings.internal

import org.springframework.stereotype.Service

@Service
class ApplicationSettings(private val repository: ApplicationSettingRepository) {
  fun <T : Any> get(key: SettingKey<T>): T? {
    val setting = repository.findById(key.name).orElse(null) ?: return null
    return key.parse(setting.value)
  }

  fun <T : Any> set(key: SettingKey<T>, value: T) {
    repository.save(ApplicationSetting(key.name, key.format(value)))
  }
}
