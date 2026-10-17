package com.sporesync.filestate

/** A fact about a local file that must survive restarts and H2 resets. */
enum class FileMarker(
    /** Name of the directory under `.sporesync` that holds this marker's files. */
    val directoryName: String,
) {
    DOWNLOADED("downloaded"),
    EXTRACTED("extracted"),
}
