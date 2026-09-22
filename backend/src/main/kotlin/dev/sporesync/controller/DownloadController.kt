package dev.sporesync.controller

import dev.sporesync.model.download.DownloadJob
import dev.sporesync.model.download.DownloadJobRepository
import dev.sporesync.model.download.DownloadRequest
import dev.sporesync.model.download.DownloadRequests
import dev.sporesync.model.download.JobAction
import dev.sporesync.model.inventory.InventoryException
import dev.sporesync.model.inventory.InventoryFailure
import org.springframework.http.HttpStatus
import org.springframework.web.bind.annotation.*
import org.springframework.web.server.ResponseStatusException

@RestController
@RequestMapping("/api/downloads")
class DownloadController(
    private val jobs: DownloadJobRepository,
    private val requests: DownloadRequests,
) {
  @GetMapping fun list(): List<DownloadJob> = jobs.list()

  @PostMapping
  @ResponseStatus(HttpStatus.ACCEPTED)
  fun enqueue(@RequestBody request: DownloadRequest): DownloadJob {
    return requests.enqueue(request)
        ?: throw ResponseStatusException(HttpStatus.NOT_FOUND, "Discovered regular file required")
  }

  @PostMapping("/{id}/cancel")
  fun cancel(@PathVariable id: String): DownloadJob = jobs.cancel(id) ?: missing()

  @PostMapping("/{id}/retry")
  fun retry(@PathVariable id: String): DownloadJob = jobs.retry(id) ?: missing()

  @PostMapping("/{id}/pause")
  fun pause(@PathVariable id: String): DownloadJob =
      jobs.requestAction(id, JobAction.PAUSE) ?: missing()

  @PostMapping("/{id}/resume")
  fun resume(@PathVariable id: String): DownloadJob = jobs.resume(id) ?: missing()

  @PostMapping("/{id}/delete-local")
  fun deleteLocal(@PathVariable id: String): DownloadJob =
      jobs.requestAction(id, JobAction.DELETE_LOCAL) ?: missing()

  @PostMapping("/{id}/delete-remote")
  fun deleteRemote(@PathVariable id: String): DownloadJob =
      jobs.requestAction(id, JobAction.DELETE_REMOTE) ?: missing()

  private fun missing(): Nothing = throw ResponseStatusException(HttpStatus.NOT_FOUND)

  @ExceptionHandler(IllegalArgumentException::class)
  @ResponseStatus(HttpStatus.BAD_REQUEST)
  fun invalid(): Map<String, String> = mapOf("error" to "INVALID_DOWNLOAD")

  @ExceptionHandler(InventoryException::class)
  @ResponseStatus(HttpStatus.BAD_GATEWAY)
  fun unavailable(error: InventoryException): Map<String, InventoryFailure> =
      mapOf("error" to error.code)
}
