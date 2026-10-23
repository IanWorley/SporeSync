package com.sporesync.localscan

import java.nio.file.Path

/** Published after every completed walk of [root]. [files] holds every entry except `.sporesync` markers. */
data class LocalScanned(
    val root: Path,
    val files: List<LocalFile>,
)
