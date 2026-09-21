package dev.sporesync.model.download

import java.nio.channels.SeekableByteChannel

interface DownloadStorage {
  fun open(destination: String, relative: String, temporary: Boolean): DownloadTarget
}

interface DownloadTarget : AutoCloseable {
  val file: DownloadFile

  fun publish()
}

interface DownloadFile : SeekableByteChannel {
  fun force()
}
