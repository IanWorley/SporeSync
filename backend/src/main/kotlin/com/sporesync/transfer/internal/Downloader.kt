package com.sporesync.transfer.internal

/** Downloads one queued path. Split from the queue so scheduling, stop, and events are testable without SSH. */
internal fun interface Downloader {
    /**
     * Downloads [path], a file or directory relative to the remote root, into the matching local path. Returns once
     * every file is complete. Must respond to thread interruption, which is how a transfer is stopped.
     */
    fun download(
        path: String,
        progress: ProgressTracker,
    )
}
