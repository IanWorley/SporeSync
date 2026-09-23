package dev.sporesync.ssh

import dev.sporesync.ModuleTestDatabase
import org.junit.jupiter.api.Assertions.assertEquals
import org.junit.jupiter.api.Assertions.assertFalse
import org.junit.jupiter.api.Test
import org.springframework.beans.factory.annotation.Autowired
import org.springframework.context.annotation.Import
import org.springframework.modulith.test.ApplicationModuleTest
import org.springframework.test.context.TestPropertySource

@ApplicationModuleTest
@Import(ModuleTestDatabase::class)
@TestPropertySource(
    properties =
        [
            "sporesync.background.enabled=false",
            "sporesync.ssh.authentication=PASSWORD",
            "sporesync.ssh.password=test-only-password",
        ]
)
class SshModuleTest {
  @Autowired private lateinit var credentials: SshSettings

  @Test
  fun `external authentication configuration binds without exposing its secret in diagnostics`() {
    assertEquals(SshAuthentication.PASSWORD, credentials.authentication)
    assertEquals("test-only-password", credentials.password)
    assertFalse(credentials.toString().contains("test-only-password"))
  }
}
