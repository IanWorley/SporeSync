package dev.sporesync.inventory

import dev.sporesync.ModuleTestDatabase
import dev.sporesync.settings.DEFAULT_SSH_PORT
import dev.sporesync.settings.DEFAULT_TIMEOUT_MILLIS
import dev.sporesync.settings.SshConnectionSettings
import dev.sporesync.ssh.SshSettings
import org.junit.jupiter.api.Assertions.assertEquals
import org.junit.jupiter.api.Assertions.assertNull
import org.junit.jupiter.api.Assertions.assertThrows
import org.junit.jupiter.api.Test
import org.mockito.Mockito.doThrow
import org.springframework.beans.factory.annotation.Autowired
import org.springframework.context.annotation.Import
import org.springframework.modulith.test.ApplicationModuleTest
import org.springframework.test.context.TestPropertySource
import org.springframework.test.context.bean.override.mockito.MockitoBean

@ApplicationModuleTest
@Import(ModuleTestDatabase::class)
@TestPropertySource(properties = ["sporesync.background.enabled=false"])
class InventoryModuleTest {
  @Autowired private lateinit var scanner: InventoryScanner
  @MockitoBean private lateinit var credentials: SshSettings

  @Test
  fun `credential failures expose only the inventory error category`() {
    doThrow(IllegalArgumentException("private credential material")).`when`(credentials).validate()
    val connection =
        SshConnectionSettings(
            "seedbox.example",
            DEFAULT_SSH_PORT,
            "scanner",
            "/remote",
            DEFAULT_TIMEOUT_MILLIS,
        )

    val failure = assertThrows(InventoryException::class.java) { scanner.scan(connection) }

    assertEquals(InventoryFailure.CONFIGURATION, failure.code)
    assertEquals("Remote inventory failed: CONFIGURATION", failure.message)
    assertNull(failure.cause)
  }
}
