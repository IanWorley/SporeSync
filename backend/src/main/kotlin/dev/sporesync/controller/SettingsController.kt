package dev.sporesync.controller

import dev.sporesync.model.settings.DownloadSettings
import dev.sporesync.model.settings.DownloadSettingsStore
import org.springframework.http.HttpStatus
import org.springframework.web.bind.annotation.*

@RestController
@RequestMapping("/api/settings")
class SettingsController(private val configuration: DownloadSettingsStore) {
  @GetMapping fun read(): DownloadSettings = configuration.read()

  @PutMapping
  fun save(@RequestBody settings: DownloadSettings): DownloadSettings = configuration.save(settings)

  @ExceptionHandler(IllegalArgumentException::class)
  @ResponseStatus(HttpStatus.BAD_REQUEST)
  fun invalid(): Map<String, String> = mapOf("error" to "INVALID_SETTINGS")
}
