package com.sporesync.localscan

import java.time.Instant

/**
 * One entry under the local download directory. [path] is relative to the scanned root and always separated by
 * `/`, so it compares directly with the remote scanner's paths. A directory's [size] is the total of the files
 * under it.
 */
data class LocalFile(
    val path: String,
    val size: Long,
    val modified: Instant,
    val isDirectory: Boolean,
)
