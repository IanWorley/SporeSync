package dev.sporesync.downloads.internal

import java.nio.channels.SeekableByteChannel

interface DownloadStorage {
  fun open(destination: String, relative: String, temporary: Boolean): DownloadTarget

  fun delete(destination: String, relative: String)

  fun missing(destination: String, relative: String): Boolean
}

interface DownloadTarget : AutoCloseable {
  val file: DownloadFile

  fun publish()
}

interface DownloadFile : SeekableByteChannel {
  fun force()
}
