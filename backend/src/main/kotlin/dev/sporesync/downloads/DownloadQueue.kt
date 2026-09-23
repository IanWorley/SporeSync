package dev.sporesync.downloads

interface DownloadQueue {
  fun acceptStable(spec: DownloadSpec)
}
