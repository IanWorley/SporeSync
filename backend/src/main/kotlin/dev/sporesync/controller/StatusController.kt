package dev.sporesync.controller

import dev.sporesync.model.ApplicationStatus
import org.springframework.web.bind.annotation.GetMapping
import org.springframework.web.bind.annotation.RestController

@RestController
class StatusController {
  /** Returns the application identity exposed by `GET /api/status`. */
  @GetMapping("/api/status") fun status(): ApplicationStatus = ApplicationStatus(SERVICE_NAME)

  private companion object {
    const val SERVICE_NAME = "sporesync"
  }
}
