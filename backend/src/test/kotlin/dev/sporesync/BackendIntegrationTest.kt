package dev.sporesync

import dev.sporesync.config.SshSettingKeys
import dev.sporesync.config.SshSettings
import dev.sporesync.model.ApplicationStatus
import dev.sporesync.model.inventory.EntryType
import dev.sporesync.model.inventory.Inventory
import dev.sporesync.model.inventory.InventoryException
import dev.sporesync.model.inventory.InventoryFailure
import dev.sporesync.model.inventory.RemoteInventory
import dev.sporesync.model.settings.ApplicationSetting
import dev.sporesync.model.settings.ApplicationSettingRepository
import dev.sporesync.model.settings.ApplicationSettings
import dev.sporesync.model.settings.DownloadConfiguration
import dev.sporesync.model.settings.DownloadSettings
import dev.sporesync.model.settings.MAX_TIMEOUT_MILLIS
