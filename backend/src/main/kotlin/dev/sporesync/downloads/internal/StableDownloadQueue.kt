package dev.sporesync.downloads.internal

import dev.sporesync.downloads.DownloadQueue
import dev.sporesync.downloads.DownloadSpec
import org.springframework.stereotype.Service

@Service
class StableDownloadQueue(
    private val jobs: DownloadJobRepository,
    private val storage: DownloadStorage,
) : DownloadQueue {
  override fun acceptStable(spec: DownloadSpec) {
    val job = jobs.enqueue(spec)
    if (job.state != JobState.COMPLETE || job.action != null) return
    val missing =
        try {
          storage.missing(spec.settings.destination, spec.entry.path)
        } catch (_: Exception) {
          false
        }
    if (missing) jobs.requeueMissing(job.id)
  }
}
