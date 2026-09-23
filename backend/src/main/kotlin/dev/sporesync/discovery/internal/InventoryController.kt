package dev.sporesync.discovery.internal

import dev.sporesync.inventory.Inventory
import dev.sporesync.inventory.InventoryException
import dev.sporesync.inventory.InventoryFailure
import org.springframework.http.HttpStatus
import org.springframework.web.bind.annotation.ExceptionHandler
import org.springframework.web.bind.annotation.PostMapping
import org.springframework.web.bind.annotation.ResponseStatus
import org.springframework.web.bind.annotation.RestController

@RestController
class InventoryController(private val discovery: Discovery) {
  @PostMapping("/api/inventory/scan") fun scan(): Inventory = discovery.scan()

  @ExceptionHandler(IllegalArgumentException::class)
  @ResponseStatus(HttpStatus.BAD_REQUEST)
  fun invalid(): Map<String, String> = mapOf("error" to "CONFIGURATION")

  @ExceptionHandler(InventoryException::class)
  @ResponseStatus(HttpStatus.BAD_GATEWAY)
  fun failed(error: InventoryException): Map<String, InventoryFailure> =
      mapOf("error" to error.code)
}
