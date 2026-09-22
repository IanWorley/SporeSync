package dev.sporesync.controller

import dev.sporesync.model.inventory.Discovery
import dev.sporesync.model.inventory.DiscoverySnapshot
import org.springframework.web.bind.annotation.GetMapping
import org.springframework.web.bind.annotation.RestController

@RestController
class DiscoveryController(private val discovery: Discovery) {
  @GetMapping("/api/inventory") fun state(): DiscoverySnapshot = discovery.state()
}
