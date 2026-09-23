package dev.sporesync

import com.tngtech.archunit.core.importer.ImportOption
import dev.sporesync.discovery.internal.Discovery
import dev.sporesync.downloads.internal.DownloadJobRepository
import dev.sporesync.downloads.internal.DownloadStorage
import dev.sporesync.downloads.internal.DownloadWorker
import dev.sporesync.inventory.internal.RemoteInventory
import dev.sporesync.settings.internal.ApplicationSettingRepository
import org.junit.jupiter.api.Assertions.assertEquals
import org.junit.jupiter.api.Assertions.assertFalse
import org.junit.jupiter.api.Assertions.assertThrows
import org.junit.jupiter.api.Assertions.assertTrue
import org.junit.jupiter.api.Test
import org.springframework.modulith.core.ApplicationModules
import org.springframework.modulith.core.Violations

class ModularityTest {
  @Test
  fun `production modules obey their declared boundaries`() {
    val modules = ApplicationModules.of(Application::class.java, ImportOption.DoNotIncludeTests())

    assertEquals(EXPECTED_MODULES, modules.map { it.identifier.toString() }.toSet())
    modules.forEach { assertFalse(it.isOpen, "${it.identifier} must hide its internals") }
    modules.verify()
  }

  @Test
  fun `implementation types are not exported as module APIs`() {
    val modules = ApplicationModules.of(Application::class.java)
    val implementations =
        listOf(
            ApplicationSettingRepository::class.java,
            RemoteInventory::class.java,
            DownloadJobRepository::class.java,
            DownloadStorage::class.java,
            DownloadWorker::class.java,
            Discovery::class.java,
        )

    implementations.forEach { type ->
      assertFalse(modules.getModuleByType(type).orElseThrow().isExposed(type), type.name)
    }
  }

  @Test
  fun `settings cannot depend on SSH`() {
    assertProbeFails(
        "SettingsDependsOnSshProbe",
        "Module 'settings' depends on module 'ssh'",
    )
  }

  @Test
  fun `discovery cannot access download internals`() {
    assertProbeFails(
        "DiscoveryAccessesDownloadInternalsProbe",
        "Module 'discovery' depends on non-exposed type",
    )
  }

  @Test
  fun `inventory cannot create a cycle through downloads`() {
    assertProbeFails("InventoryDependsOnDownloadsProbe", "Cycle detected")
  }

  private fun assertProbeFails(probeClass: String, expectedMessage: String) {
    val production = ImportOption.DoNotIncludeTests()
    val modules =
        ApplicationModules.of(
            Application::class.java,
            ImportOption { location ->
              production.includes(location) || location.contains("/$probeClass.class")
            },
        )

    val violations = assertThrows(Violations::class.java) { modules.verify() }

    assertTrue(
        violations.message.orEmpty().contains(expectedMessage),
        "Expected '$expectedMessage' in:\n${violations.message}",
    )
  }

  private companion object {
    val EXPECTED_MODULES = setOf("settings", "ssh", "inventory", "downloads", "discovery")
  }
}
