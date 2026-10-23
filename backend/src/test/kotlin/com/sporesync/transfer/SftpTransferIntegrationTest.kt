package com.sporesync.transfer

import com.sporesync.config.AuthMethod
import com.sporesync.config.Settings
import com.sporesync.config.SettingsStore
import com.sporesync.transfer.internal.SftpDownloader
import org.junit.jupiter.api.AfterAll
import org.junit.jupiter.api.Assertions.assertArrayEquals
import org.junit.jupiter.api.Assertions.assertEquals
import org.junit.jupiter.api.Assertions.assertFalse
import org.junit.jupiter.api.Assertions.assertTrue
import org.junit.jupiter.api.BeforeAll
import org.junit.jupiter.api.Test
import org.junit.jupiter.api.io.TempDir
import org.springframework.context.ApplicationEventPublisher
import org.springframework.mock.env.MockEnvironment
import org.springframework.util.unit.DataSize
import org.testcontainers.containers.GenericContainer
import org.testcontainers.containers.wait.strategy.Wait
import org.testcontainers.images.builder.Transferable
import org.testcontainers.utility.DockerImageName
import java.nio.file.Files
import java.nio.file.Path
import java.security.MessageDigest
import java.time.Duration
import kotlin.random.Random

// Multi-arch, so the test runs natively on both CI runners and Apple Silicon.
private const val SSH_IMAGE = "lscr.io/linuxserver/openssh-server:10.3_p1-r1-ls239"
private const val SSH_PORT = 2222
private const val SSH_USER = "sporesync"
private const val SSH_PASSWORD = "sporesync-test"
private const val READY_LOG = ".*\\[ls.io-init\\] done.*"
private const val REMOTE_DIR = "/data"
private val FILE_MODE = "644".toInt(radix = 8)
private const val HOST_PUBLIC_KEY = "/config/ssh_host_keys/ssh_host_ed25519_key.pub"
private const val IMPOSTOR_KEY = "/tmp/impostor"
private const val KNOWN_HOSTS_FILE_NAME = "known_hosts"

private const val SETTINGS_FILE_NAME = "settings.yaml"
private const val SHA_256 = "SHA-256"
private const val RANDOM_SEED = 13
private val TIMEOUT: Duration = Duration.ofSeconds(60)

private const val TREE = "show"
private const val MOVIE = "movie.bin"
private const val SHRINKING = "shrinking.bin"
private const val EMPTY = "empty.bin"
private const val SYMLINK = "hostname-link"
private const val SYMLINK_TARGET = "/etc/hostname"
private const val SYMLINK_ANCESTOR = "outside-link"
private const val STALE_CONTENT = "stale"
private const val MOUNTED_DOWNLOADS = "mounted-downloads"
private val REMOTE_FILES: Map<String, Int> =
    mapOf(
        "$TREE/season-1/episode-1.bin" to DataSize.ofMegabytes(2).toBytes().toInt(),
        "$TREE/season-1/episode-2.bin" to DataSize.ofMegabytes(1).toBytes().toInt(),
        "$TREE/season-2/episode-1.bin" to DataSize.ofKilobytes(512).toBytes().toInt(),
        "$TREE/notes.txt" to DataSize.ofKilobytes(1).toBytes().toInt(),
        MOVIE to DataSize.ofMegabytes(4).toBytes().toInt(),
    )

// Slow enough that the movie is still downloading when stopped, fast enough to keep the test short.
private val BANDWIDTH_LIMIT: DataSize = DataSize.ofMegabytes(1)
private val STOP_AFTER_BYTES = DataSize.ofKilobytes(1536).toBytes()

// One progress interval may move a chunk ahead of the cap, so allow some slack over the configured rate.
private const val BANDWIDTH_TOLERANCE = 1.25

class SftpTransferIntegrationTest {
    @TempDir
    private lateinit var directory: Path

    private val downloads: Path get() = directory.resolve("downloads")

    @Test
    fun `downloads a directory tree and a file concurrently with matching checksums`() {
        val settings = settings(bandwidthLimit = null)
        val events = RecordingEvents()

        transferQueue(settings, events) { queue ->
            queue.enqueue(TREE)
            queue.enqueue(MOVIE)
            events.await<TransferCompleted>(TIMEOUT) { it.path == TREE }
            events.await<TransferCompleted>(TIMEOUT) { it.path == MOVIE }
        }

        assertEquals(emptyList<TransferFailed>(), events.ofType<TransferFailed>())
        REMOTE_FILES.keys.forEach(::assertDownloaded)
        val treeBytes = REMOTE_FILES.filterKeys { it.startsWith("$TREE/") }.values.sumOf { it.toLong() }
        assertEquals(treeBytes, events.ofType<TransferProgress>().first { it.path == TREE }.totalBytes)
    }

    @Test
    fun `a stopped download resumes from the partial file after a restart`() {
        val settings = settings(BANDWIDTH_LIMIT)
        val firstRun = RecordingEvents()
        transferQueue(settings, firstRun) { queue ->
            queue.enqueue(MOVIE)
            firstRun.await<TransferProgress>(TIMEOUT) { it.bytesTransferred >= STOP_AFTER_BYTES }
            queue.stop(MOVIE)
            firstRun.await<TransferStopped>(TIMEOUT)
        }
        val partialSize = Files.size(downloads.resolve(MOVIE))
        val cap = BANDWIDTH_LIMIT.toBytes() * BANDWIDTH_TOLERANCE

        settings.update { it.copy(transfer = it.transfer.copy(bandwidthLimit = null)) }
        val secondRun = RecordingEvents()
        transferQueue(settings, secondRun) { queue ->
            queue.enqueue(MOVIE)
            secondRun.await<TransferCompleted>(TIMEOUT)
        }

        assertTrue(
            partialSize in STOP_AFTER_BYTES until REMOTE_FILES.getValue(MOVIE).toLong(),
        ) { "partial size $partialSize" }
        assertTrue(firstRun.ofType<TransferProgress>().all { it.bytesPerSecond <= cap }) { "${firstRun.all()}" }
        assertEquals(partialSize, secondRun.ofType<TransferProgress>().first().bytesTransferred)
        assertDownloaded(MOVIE)
    }

    @Test
    fun `refuses a server whose host key is not in known_hosts`() {
        val knownHosts = directory.resolve(KNOWN_HOSTS_FILE_NAME)

        val failure = downloadExpectingFailure(MOVIE, knownHosts)

        assertTrue(failure.reason.contains(knownHosts.toString())) { failure.reason }
        assertFalse(Files.exists(downloads.resolve(MOVIE)))
    }

    @Test
    fun `refuses a server whose host key differs from the one in known_hosts`() {
        exec("sh", "-c", "rm -f $IMPOSTOR_KEY* && ssh-keygen -q -t ed25519 -N '' -f $IMPOSTOR_KEY")
        val knownHosts = knownHosts(exec("cat", "$IMPOSTOR_KEY.pub"))

        val failure = downloadExpectingFailure(MOVIE, knownHosts)

        assertTrue(failure.reason.contains(knownHosts.toString())) { failure.reason }
        assertFalse(Files.exists(downloads.resolve(MOVIE)))
    }

    @Test
    fun `an empty remote file replaces a stale local copy`() {
        exec("touch", "$REMOTE_DIR/$EMPTY")
        Files.createDirectories(downloads)
        Files.writeString(downloads.resolve(EMPTY), STALE_CONTENT)
        val events = RecordingEvents()

        transferQueue(settings(bandwidthLimit = null), events) { queue ->
            queue.enqueue(EMPTY)
            events.await<TransferCompleted>(TIMEOUT)
        }

        assertEquals(0L, Files.size(downloads.resolve(EMPTY)))
    }

    @Test
    fun `a download directory that is itself a symlink still receives downloads`() {
        exec("touch", "$REMOTE_DIR/$EMPTY")
        val mounted = Files.createDirectories(directory.resolve(MOUNTED_DOWNLOADS))
        Files.createSymbolicLink(downloads, mounted)
        val events = RecordingEvents()

        transferQueue(settings(bandwidthLimit = null), events) { queue ->
            queue.enqueue(EMPTY)
            events.awaitAny(TIMEOUT, "terminal event") { it is TransferCompleted || it is TransferFailed }
        }

        assertEquals(listOf(EMPTY), events.ofType<TransferCompleted>().map { it.path })
        assertTrue(Files.isRegularFile(mounted.resolve(EMPTY)))
    }

    @Test
    fun `a remote file that shrinks mid-download fails instead of completing`() {
        exec("cp", "$REMOTE_DIR/$MOVIE", "$REMOTE_DIR/$SHRINKING")
        val events = RecordingEvents()

        transferQueue(settings(BANDWIDTH_LIMIT), events) { queue ->
            queue.enqueue(SHRINKING)
            events.await<TransferProgress>(TIMEOUT) { it.bytesTransferred >= STOP_AFTER_BYTES }
            exec("truncate", "-s", "0", "$REMOTE_DIR/$SHRINKING")
            events.awaitAny(TIMEOUT, "terminal event") { it is TransferCompleted || it is TransferFailed }
        }

        assertEquals(emptyList<TransferCompleted>(), events.ofType<TransferCompleted>())
        assertEquals(listOf(SHRINKING), events.ofType<TransferFailed>().map { it.path })
    }

    @Test
    fun `a queued path that is a remote symlink is refused`() {
        exec("ln", "-sf", SYMLINK_TARGET, "$REMOTE_DIR/$SYMLINK")

        val failure = downloadExpectingFailure(SYMLINK, trustedKnownHosts())

        assertEquals(SYMLINK, failure.path)
        assertFalse(Files.exists(downloads.resolve(SYMLINK)))
    }

    @Test
    fun `a queued path through a remote symlink ancestor is refused`() {
        exec("ln", "-sfn", "/etc", "$REMOTE_DIR/$SYMLINK_ANCESTOR")

        val failure = downloadExpectingFailure("$SYMLINK_ANCESTOR/hostname", trustedKnownHosts())

        assertEquals("$SYMLINK_ANCESTOR/hostname", failure.path)
        assertFalse(Files.exists(downloads.resolve(SYMLINK_ANCESTOR)))
    }

    @Test
    fun `a local symlink target cannot truncate an outside file`() {
        exec("touch", "$REMOTE_DIR/$EMPTY")
        Files.createDirectories(downloads)
        val outside = Files.writeString(directory.resolve("outside.txt"), STALE_CONTENT)
        Files.createSymbolicLink(downloads.resolve(EMPTY), outside)

        val events = RecordingEvents()
        transferQueue(settings(bandwidthLimit = null), events) { queue ->
            queue.enqueue(EMPTY)
            events.awaitAny(TIMEOUT, "terminal event") { it is TransferCompleted || it is TransferFailed }
        }

        assertEquals(STALE_CONTENT, Files.readString(outside))
        assertEquals(listOf(EMPTY), events.ofType<TransferFailed>().map { it.path })
        assertEquals(emptyList<TransferCompleted>(), events.ofType<TransferCompleted>())
    }

    @Test
    fun `a local symlink ancestor cannot create files outside the download directory`() {
        Files.createDirectories(downloads)
        val outside = Files.createDirectory(directory.resolve("outside"))
        Files.createSymbolicLink(downloads.resolve(TREE), outside)

        val events = RecordingEvents()
        transferQueue(settings(bandwidthLimit = null), events) { queue ->
            queue.enqueue(TREE)
            events.awaitAny(TIMEOUT, "terminal event") { it is TransferCompleted || it is TransferFailed }
        }

        Files.list(outside).use { assertEquals(emptyList<Path>(), it.toList()) }
        assertEquals(listOf(TREE), events.ofType<TransferFailed>().map { it.path })
        assertEquals(emptyList<TransferCompleted>(), events.ofType<TransferCompleted>())
    }

    private fun downloadExpectingFailure(
        path: String,
        knownHosts: Path,
    ): TransferFailed {
        val events = RecordingEvents()
        transferQueue(settings(bandwidthLimit = null), events, knownHosts) { queue ->
            queue.enqueue(path)
            events.awaitAny(TIMEOUT, "terminal event") { it is TransferCompleted || it is TransferFailed }
        }
        return events.ofType<TransferFailed>().single()
    }

    private fun knownHosts(publicKey: String): Path {
        val line = "[${server.host}]:${server.getMappedPort(SSH_PORT)} $publicKey"
        return Files.writeString(directory.resolve(KNOWN_HOSTS_FILE_NAME), line)
    }

    private fun trustedKnownHosts(): Path = knownHosts(exec("cat", HOST_PUBLIC_KEY))

    private fun assertDownloaded(path: String) {
        assertArrayEquals(sha256(contents.getValue(path)), sha256(Files.readAllBytes(downloads.resolve(path)))) {
            "checksum of $path"
        }
    }

    private fun settings(bandwidthLimit: DataSize?): SettingsStore {
        val store =
            SettingsStore(MockEnvironment(), ApplicationEventPublisher { }, directory.resolve(SETTINGS_FILE_NAME))
        store.update { current ->
            Settings(
                connection =
                    current.connection.copy(
                        host = server.host,
                        port = server.getMappedPort(SSH_PORT),
                        username = SSH_USER,
                        authMethod = AuthMethod.PASSWORD,
                        password = SSH_PASSWORD,
                    ),
                paths = current.paths.copy(remoteDir = REMOTE_DIR, localDir = downloads),
                transfer = current.transfer.copy(bandwidthLimit = bandwidthLimit),
            )
        }
        return store
    }

    /** Runs [block] against a fresh queue and downloader, closing both after: a restart in miniature. */
    private fun transferQueue(
        settings: SettingsStore,
        events: ApplicationEventPublisher,
        knownHosts: Path = trustedKnownHosts(),
        block: (TransferQueue) -> Unit,
    ) {
        SftpDownloader(settings, knownHosts).use { downloader ->
            TransferQueue(settings, events, downloader).use(block)
        }
    }

    private class SshServer : GenericContainer<SshServer>(DockerImageName.parse(SSH_IMAGE))

    companion object {
        private val random = Random(RANDOM_SEED)
        private val contents: Map<String, ByteArray> = REMOTE_FILES.mapValues { (_, size) -> random.nextBytes(size) }

        private val server =
            SshServer()
                .withEnv("USER_NAME", SSH_USER)
                .withEnv("USER_PASSWORD", SSH_PASSWORD)
                .withEnv("PASSWORD_ACCESS", "true")
                .withExposedPorts(SSH_PORT)
                .waitingFor(Wait.forLogMessage(READY_LOG, 1))
                .apply {
                    contents.forEach { (path, bytes) ->
                        withCopyToContainer(Transferable.of(bytes, FILE_MODE), "$REMOTE_DIR/$path")
                    }
                }

        @JvmStatic
        @BeforeAll
        fun startServer() = server.start()

        @JvmStatic
        @AfterAll
        fun stopServer() = server.stop()

        private fun sha256(bytes: ByteArray): ByteArray = MessageDigest.getInstance(SHA_256).digest(bytes)

        private fun exec(vararg command: String): String {
            val result = server.execInContainer(*command)
            check(result.exitCode == 0) { "${command.joinToString(" ")} failed: ${result.stderr}" }
            return result.stdout.trim()
        }
    }
}
