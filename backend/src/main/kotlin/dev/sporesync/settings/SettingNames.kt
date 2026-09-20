package dev.sporesync.settings

/** Stable storage names; consuming features define typed keys, validation, and defaults. */
object SettingNames {
  const val SSH_HOST = "ssh.host"
  const val SSH_PORT = "ssh.port"
  const val SSH_USERNAME = "ssh.username"
  const val REMOTE_SOURCE_DIRECTORY = "remote.source.directory"
  const val LOCAL_DOWNLOAD_DIRECTORY = "local.download.directory"
  const val SCAN_INTERVAL = "scan.interval"
  const val AUTOMATIC_DOWNLOAD_ENABLED = "download.automatic.enabled"
  const val TEMPORARY_FILE_ENABLED = "download.temporary-file.enabled"
}
