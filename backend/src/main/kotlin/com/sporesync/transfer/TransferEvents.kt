package com.sporesync.transfer

import java.time.Duration

// Every event names the transfer by the normalized path given to TransferQueue.enqueue, relative to the remote root.

/**
 * Throttled progress of an in-flight transfer. [bytesTransferred] includes bytes resumed from a partial download;
 * [bytesPerSecond] covers only what moved since the previous progress event. [eta] is `null` while nothing moves.
 */
data class TransferProgress(
    val path: String,
    val bytesTransferred: Long,
    val totalBytes: Long,
    val bytesPerSecond: Long,
    val eta: Duration?,
)

/** Every file under [path] is now fully downloaded. */
data class TransferCompleted(
    val path: String,
)

/** The transfer of [path] stopped on an error. Partial files are kept so a later enqueue resumes them. */
data class TransferFailed(
    val path: String,
    val reason: String,
)

/** [TransferQueue.stop] removed [path] from the queue or interrupted it. Partial files are kept for resume. */
data class TransferStopped(
    val path: String,
)
