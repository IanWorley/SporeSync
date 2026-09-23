package dev.sporesync.settings

import dev.sporesync.ModuleTestDatabase
import org.junit.jupiter.api.Assertions.assertEquals
import org.junit.jupiter.api.Assertions.assertThrows
import org.junit.jupiter.api.Test
import org.springframework.beans.factory.annotation.Autowired
import org.springframework.context.annotation.Import
import org.springframework.modulith.test.ApplicationModuleTest
import org.springframework.test.context.TestPropertySource

@ApplicationModuleTest
@Import(ModuleTestDatabase::class)
@TestPropertySource(properties = ["sporesync.background.enabled=false"])
class SettingsModuleTest {
  @Autowired private lateinit var settings: DownloadSettingsStore

  private val configured =
      DownloadSettings(
          host = "seedbox.example",
          username = "scanner",
          source = "/remote",
          destination = "/local",
      )

  @Test
  fun `saved settings can be read through the module contract`() {
    settings.save(configured)

    assertEquals(configured, settings.read())
    assertEquals(
        SshConnectionSettings(
            "seedbox.example",
            DEFAULT_SSH_PORT,
            "scanner",
            "/remote",
            DEFAULT_TIMEOUT_MILLIS,
        ),
        settings.read().connection(),
    )
  }

  @Test
  fun `invalid settings leave the previous configuration intact`() {
    settings.save(configured)

    assertThrows(IllegalArgumentException::class.java) {
      settings.save(configured.copy(host = "changed.example", destination = "relative"))
    }

    assertEquals(configured, settings.read())
  }
}
