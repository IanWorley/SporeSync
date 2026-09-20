package dev.sporesync

import org.springframework.http.HttpStatus
import org.springframework.web.bind.annotation.*
import org.springframework.web.server.ResponseStatusException

data class DownloadRequest(val path: String)

@RestController
@RequestMapping("/api/downloads")
class DownloadController(
    private val jobs: DownloadJobs,
    private val inventory: RemoteInventory,
    private val configuration: DownloadConfiguration,
) {
  @GetMapping fun list(): List<DownloadJob> = jobs.list()

  @PostMapping
  @ResponseStatus(HttpStatus.ACCEPTED)
  fun enqueue(@RequestBody request: DownloadRequest): DownloadJob {
    val settings = configuration.read()
    settings.validate()
    val connection =
        SshConnectionSettings(
            settings.host,
            settings.port,
            settings.username,
            settings.source,
            DEFAULT_TIMEOUT_MILLIS,
        )
    val entry =
        inventory.scan(connection).entries.singleOrNull {
          it.path == request.path && it.type == EntryType.file
        } ?: throw ResponseStatusException(HttpStatus.NOT_FOUND, "Discovered regular file required")
    return jobs.enqueue(DownloadSpec(settings, entry))
  }

  @PostMapping("/{id}/cancel")
  fun cancel(@PathVariable id: String): DownloadJob = jobs.cancel(id) ?: missing()

  @PostMapping("/{id}/retry")
  fun retry(@PathVariable id: String): DownloadJob = jobs.retry(id) ?: missing()

  private fun missing(): Nothing = throw ResponseStatusException(HttpStatus.NOT_FOUND)

  @ExceptionHandler(IllegalArgumentException::class)
  @ResponseStatus(HttpStatus.BAD_REQUEST)
  fun invalid(): Map<String, String> = mapOf("error" to "INVALID_DOWNLOAD")

  @ExceptionHandler(InventoryException::class)
  @ResponseStatus(HttpStatus.BAD_GATEWAY)
  fun unavailable(error: InventoryException): Map<String, InventoryFailure> =
      mapOf("error" to error.code)
}
