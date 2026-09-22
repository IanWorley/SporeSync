package dev.sporesync.model.download

/** Persists download state; callers hold the worker lock during recovery. */
interface DownloadJobRepository {
  fun enqueue(spec: DownloadSpec): DownloadJob

  fun list(): List<DownloadJob>

  fun find(id: String): DownloadJob?

  fun recover()

  fun next(): DownloadJob?

  fun start(id: String): Boolean

  fun progress(id: String, bytes: Long)

  fun finish(id: String, state: JobState, error: String? = null)

  fun cancel(id: String): DownloadJob?

  fun retry(id: String): DownloadJob?

  fun requestAction(id: String, action: JobAction): DownloadJob?

  fun resume(id: String): DownloadJob?

  fun requeueMissing(id: String)

  fun completeAction(job: DownloadJob, error: String? = null)
}
