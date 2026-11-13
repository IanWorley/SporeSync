package com.sporesync.localscan

import java.nio.file.Path

/** Published after every completed walk of [root]. [files] leaves out the `.sporesync` filestate directory. */
data class LocalScanned(
    val root: Path,
    val files: List<LocalFile>,
)
