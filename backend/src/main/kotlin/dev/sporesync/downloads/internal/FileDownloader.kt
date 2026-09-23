package dev.sporesync.downloads.internal

import dev.sporesync.downloads.DownloadSpec

/** Transfers one file, reporting byte progress and honoring cancellation. */
interface FileDownloader {
  fun transfer(spec: DownloadSpec, progress: (Long) -> Unit, cancelled: () -> Boolean)

  fun deleteLocal(spec: DownloadSpec)

  fun deleteRemote(spec: DownloadSpec)
}
