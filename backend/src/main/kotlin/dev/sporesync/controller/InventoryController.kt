package dev.sporesync.controller

import dev.sporesync.model.inventory.Inventory
import dev.sporesync.model.inventory.InventoryException
import dev.sporesync.model.inventory.InventoryFailure
import dev.sporesync.model.inventory.RemoteInventory
import org.springframework.http.HttpStatus
import org.springframework.web.bind.annotation.ExceptionHandler
import org.springframework.web.bind.annotation.PostMapping
import org.springframework.web.bind.annotation.ResponseStatus
import org.springframework.web.bind.annotation.RestController

@RestController
class InventoryController(private val inventory: RemoteInventory) {
  @PostMapping("/api/inventory/scan") fun scan(): Inventory = inventory.scan()

  @ExceptionHandler(InventoryException::class)
  @ResponseStatus(HttpStatus.BAD_GATEWAY)
  fun failed(error: InventoryException): Map<String, InventoryFailure> =
      mapOf("error" to error.code)
}
