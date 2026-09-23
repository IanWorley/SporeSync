package dev.sporesync.discovery

import dev.sporesync.ModuleTestDatabase
import dev.sporesync.discovery.internal.Discovery
import dev.sporesync.downloads.DownloadQueue
import dev.sporesync.downloads.DownloadSpec
import dev.sporesync.inventory.EntryType
import dev.sporesync.inventory.Inventory
import dev.sporesync.inventory.InventoryEntry
import dev.sporesync.inventory.InventoryException
import dev.sporesync.inventory.InventoryFailure
import dev.sporesync.inventory.InventoryScanner
import dev.sporesync.settings.DownloadSettings
import dev.sporesync.settings.DownloadSettingsStore
import dev.sporesync.settings.SshConnectionSettings
import org.junit.jupiter.api.AfterEach
import org.junit.jupiter.api.Assertions.assertEquals
import org.junit.jupiter.api.Assertions.assertNotNull
import org.junit.jupiter.api.Assertions.assertNull
import org.junit.jupiter.api.Assertions.assertSame
import org.junit.jupiter.api.Assertions.assertThrows
import org.junit.jupiter.api.BeforeEach
import org.junit.jupiter.api.Test
import org.junit.jupiter.api.TestInfo
import org.mockito.Mockito.clearInvocations
import org.mockito.Mockito.doThrow
import org.mockito.Mockito.verify
import org.mockito.Mockito.verifyNoInteractions
import org.mockito.Mockito.verifyNoMoreInteractions
import org.mockito.Mockito.`when`
import org.springframework.beans.factory.annotation.Autowired
import org.springframework.context.annotation.Import
import org.springframework.modulith.test.ApplicationModuleTest
import org.springframework.test.context.TestPropertySource
import org.springframework.test.context.bean.override.mockito.MockitoBean

@ApplicationModuleTest
@Import(ModuleTestDatabase::class)
@TestPropertySource(properties = ["sporesync.background.enabled=false"])
class DiscoveryModuleTest {
  @Autowired private lateinit var discovery: Discovery
  @MockitoBean private lateinit var scanner: InventoryScanner
  @MockitoBean private lateinit var configuration: DownloadSettingsStore
  @MockitoBean private lateinit var queue: DownloadQueue

  private lateinit var settings: DownloadSettings
  private lateinit var connection: SshConnectionSettings
  private val entry = InventoryEntry(FILE_PATH, EntryType.file, FILE_BYTES, MODIFIED_TIME_NS)
  private val inventory = Inventory(SCHEMA_VERSION, listOf(entry))

  @BeforeEach
  fun configureBoundaries(test: TestInfo) {
    settings =
        DownloadSettings(
            host = "seedbox.example",
            username = "scanner",
            source = "/remote/${test.testMethod.orElseThrow().name}",
            destination = "/local",
        )
    connection =
        SshConnectionSettings(
            settings.host,
            settings.port,
            settings.username,
            settings.source,
            settings.timeoutMillis,
        )
    `when`(configuration.read()).thenReturn(settings)
    `when`(scanner.scan(connection)).thenReturn(inventory)
  }

  @AfterEach
  fun rejectUnexpectedQueueCommands() {
    verifyNoMoreInteractions(queue)
  }

  @Test
  fun `two unchanged scans queue the file with its scan settings`() {
    assertEquals(inventory, discovery.scan())
    verifyNoInteractions(queue)

    assertEquals(inventory, discovery.scan())

    verify(queue).acceptStable(DownloadSpec(settings, entry))
    assertSuccessfulSnapshot(inventory)
  }

  @Test
  fun `changed settings require two scans before automatic queueing`() {
    assertEquals(inventory, discovery.scan())
    val changed = settings.copy(destination = "/changed-destination")
    `when`(configuration.read()).thenReturn(changed)

    assertEquals(inventory, discovery.scan())
    verifyNoInteractions(queue)
    assertEquals(inventory, discovery.scan())

    verify(queue).acceptStable(DownloadSpec(changed, entry))
    assertSuccessfulSnapshot(inventory)
  }

  @Test
  fun `changed file metadata requires two unchanged observations`() {
    assertEquals(inventory, discovery.scan())
    val changedEntry = entry.copy(sizeBytes = GROWN_FILE_BYTES)
    val changedInventory = Inventory(SCHEMA_VERSION, listOf(changedEntry))
    `when`(scanner.scan(connection)).thenReturn(changedInventory)

    assertEquals(changedInventory, discovery.scan())
    verifyNoInteractions(queue)
    assertEquals(changedInventory, discovery.scan())

    verify(queue).acceptStable(DownloadSpec(settings, changedEntry))
    assertSuccessfulSnapshot(changedInventory)
  }

  @Test
  fun `scanner failure interrupts consecutive stable scans`() {
    val failure = InventoryException(InventoryFailure.CONNECTION)
    `when`(scanner.scan(connection)).thenReturn(inventory).thenThrow(failure).thenReturn(inventory)
    assertEquals(inventory, discovery.scan())
    val previous = discovery.state()

    assertSame(failure, assertThrows(InventoryException::class.java) { discovery.scan() })

    assertEquals("CONNECTION", discovery.state().error)
    assertEquals(inventory, discovery.state().inventory)
    assertEquals(previous.lastSuccess, discovery.state().lastSuccess)
    assertEquals(inventory, discovery.scan())
    verifyNoInteractions(queue)
    assertEquals(inventory, discovery.scan())
    verify(queue).acceptStable(DownloadSpec(settings, entry))
    assertSuccessfulSnapshot(inventory)
  }

  @Test
  fun `queue failure propagates before scan success and resets stability`() {
    val failure = IllegalStateException("Queue unavailable")
    val spec = DownloadSpec(settings, entry)
    val laterInventory =
        Inventory(
            SCHEMA_VERSION,
            listOf(entry, entry.copy(path = "new-directory", type = EntryType.directory)),
        )
    `when`(scanner.scan(connection)).thenReturn(inventory).thenReturn(laterInventory)
    doThrow(failure).doNothing().`when`(queue).acceptStable(spec)
    assertEquals(inventory, discovery.scan())
    val previous = discovery.state()

    assertSame(failure, assertThrows(IllegalStateException::class.java) { discovery.scan() })

    assertEquals("CONFIGURATION", discovery.state().error)
    assertEquals(inventory, discovery.state().inventory)
    assertEquals(previous.lastSuccess, discovery.state().lastSuccess)
    verify(queue).acceptStable(spec)
    verifyNoMoreInteractions(queue)
    clearInvocations(queue)
    assertEquals(laterInventory, discovery.scan())
    verifyNoInteractions(queue)
    assertEquals(laterInventory, discovery.scan())
    verify(queue).acceptStable(spec)
    assertSuccessfulSnapshot(laterInventory)
  }

  @Test
  fun `automatic queue excludes reserved paths and nonregular entries`() {
    val excluded =
        listOf(
            entry.copy(path = ".sporesync/private"),
            entry.copy(path = "directory", type = EntryType.directory),
            entry.copy(path = "link", type = EntryType.symlink),
            entry.copy(path = "other", type = EntryType.other),
        )
    val mixedInventory = Inventory(SCHEMA_VERSION, listOf(entry) + excluded)
    `when`(scanner.scan(connection)).thenReturn(mixedInventory)

    assertEquals(mixedInventory, discovery.scan())
    assertEquals(mixedInventory, discovery.scan())

    verify(queue).acceptStable(DownloadSpec(settings, entry))
    assertSuccessfulSnapshot(mixedInventory)
  }

  @Test
  fun `disabled automatic downloading discovers files without a destination`() {
    val discoveryOnly = settings.copy(automatic = false, destination = "")
    `when`(configuration.read()).thenReturn(discoveryOnly)

    assertEquals(inventory, discovery.scan())
    assertEquals(inventory, discovery.scan())

    assertSuccessfulSnapshot(inventory)
    verifyNoInteractions(queue)
  }

  private fun assertSuccessfulSnapshot(expected: Inventory) {
    val state = discovery.state()
    assertEquals(expected, state.inventory)
    assertNotNull(state.lastAttempt)
    assertNotNull(state.lastSuccess)
    assertNull(state.error)
  }

  private companion object {
    const val SCHEMA_VERSION = 1
    const val FILE_PATH = "nested/file.txt"
    const val FILE_BYTES = 128L
    const val GROWN_FILE_BYTES = 256L
    const val MODIFIED_TIME_NS = 1_000_000_000L
  }
}
