package dev.sporesync

import org.springframework.web.bind.annotation.GetMapping
import org.springframework.web.bind.annotation.RestController

@RestController
class StatusController {
  @GetMapping("/api/status") fun status(): ApplicationStatus = ApplicationStatus(SERVICE_NAME)

  private companion object {
    const val SERVICE_NAME = "sporesync"
  }
}

data class ApplicationStatus(val application: String)
