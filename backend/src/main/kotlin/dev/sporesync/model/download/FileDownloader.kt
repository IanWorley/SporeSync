package dev.sporesync.model.download

/** Transfers one file, reporting byte progress and honoring cancellation. */
interface FileDownloader {
  fun transfer(spec: DownloadSpec, progress: (Long) -> Unit, cancelled: () -> Boolean)
}
