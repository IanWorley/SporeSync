package com.sporesync.remotescan

import com.sporesync.remotescan.internal.SshjSshSftpConnectionFactory
import org.junit.jupiter.api.AfterAll
import org.junit.jupiter.api.Assertions.assertEquals
import org.junit.jupiter.api.Assertions.assertFalse
import org.junit.jupiter.api.Assertions.assertNotEquals
import org.junit.jupiter.api.Assertions.assertNull
import org.junit.jupiter.api.Assertions.assertTrue
import org.junit.jupiter.api.BeforeAll
import org.junit.jupiter.api.Test
import org.junit.jupiter.api.assertThrows
import org.junit.jupiter.api.assertTimeoutPreemptively
import org.junit.jupiter.api.io.TempDir
import org.testcontainers.containers.GenericContainer
import org.testcontainers.containers.wait.strategy.Wait
import org.testcontainers.utility.DockerImageName
import tools.jackson.databind.json.JsonMapper
import java.nio.file.Files
import java.nio.file.Path
import java.time.Duration

private const val SSH_IMAGE = "lscr.io/linuxserver/openssh-server:10.3_p1-r1-ls239"
private const val SSH_PORT = 2222
private const val SSH_USER = "sporesync"
private const val SSH_PASSWORD = "sporesync-test"
private const val READY_LOG = ".*\\[ls.io-init\\] done.*"
private const val REMOTE_SCANNER = "/tmp/.sporesync/scanner.py"
private const val LARGE_TREE_ROOT = "/tmp/huge-tree"

class SshjScannerScriptSyncIntegrationTest {
    @TempDir
    private lateinit var tempDir: Path

    @Test
    fun `uploads then skips then reuploads when remote copy changes`() {
        val sync = ScannerScriptSync(EmbeddedScannerScript())
        openConnection(knownHostsFile = trustedKnownHosts()).use { connection ->
            connection.run("rm -rf /tmp/.sporesync").requireSuccess("clear remote scanner")

            val first = sync.ensureCurrent(connection, REMOTE_SCANNER)
            val second = sync.ensureCurrent(connection, REMOTE_SCANNER)
            connection.run("printf 'stale-copy\\n' > $REMOTE_SCANNER").requireSuccess("mutate scanner")
            val third = sync.ensureCurrent(connection, REMOTE_SCANNER)
            val remoteHash = requireNotNull(connection.readFile(REMOTE_SCANNER)).sha256Hex()
            println("sync:first=$first")
            println("sync:second=$second")
            println("sync:third=$third remoteHash=$remoteHash")

            assertTrue(first.uploaded)
            assertNull(first.remoteHashBeforeUpload)
            assertFalse(second.uploaded)
            assertEquals(syncScriptHash(), second.remoteHashBeforeUpload)
            assertTrue(third.uploaded)
            assertNotEquals(syncScriptHash(), third.remoteHashBeforeUpload)
            assertEquals(syncScriptHash(), remoteHash)
        }
    }

    @Test
    fun `refuses connection when known_hosts has a different host key`() {
        val exception =
            assertThrows<Exception> {
                openConnection(knownHostsFile = knownHosts(impostorPublicKey())).use { connection ->
                    connection.run("echo never")
                }
            }
        println("known-hosts-mismatch:${exception.message}")
        assertTrue(exception.message?.isNotBlank() == true)
    }

    @Test
    fun `accepts pinned fingerprint and refuses a mismatch`() {
        val fingerprint = hostFingerprint()

        openConnection(knownHostsFile = null, hostKeyFingerprint = fingerprint).use { connection ->
            val result = connection.run("echo ok")
            result.requireSuccess("echo ok")
            println("fingerprint-match:${result.stdout.trim()}")
            assertEquals("ok", result.stdout.trim())
        }

        val mismatch = fingerprint.dropLast(1) + if (fingerprint.last() == 'A') "B" else "A"
        val exception =
            assertThrows<Exception> {
                openConnection(knownHostsFile = null, hostKeyFingerprint = mismatch).use { connection ->
                    connection.run("echo never")
                }
            }
        println("fingerprint-mismatch:${exception.message}")
    }

    @Test
    fun `scanner handles a large tree without hanging`() {
        val sync = ScannerScriptSync(EmbeddedScannerScript())
        openConnection(knownHostsFile = trustedKnownHosts()).use { connection ->
            sync.ensureCurrent(connection, REMOTE_SCANNER)

            connection
                .run(
                    """
                    rm -rf $LARGE_TREE_ROOT
                    mkdir -p $LARGE_TREE_ROOT
                    for d in ${'$'}(seq -w 0 79); do
                      dir=$LARGE_TREE_ROOT/d${'$'}d
                      mkdir -p "${'$'}dir"
                      for i in ${'$'}(seq -w 0 79); do
                        printf 'xxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxx' > "${'$'}dir/f${'$'}i.txt"
                      done
                    done
                    echo ready
                    """.trimIndent(),
                ).requireSuccess("create large tree")

            val python =
                connection
                    .run("command -v python3 || command -v python")
                    .stdout
                    .lineSequence()
                    .map(String::trim)
                    .firstOrNull(String::isNotBlank)

            val command =
                if (python != null) {
                    "$python $REMOTE_SCANNER $LARGE_TREE_ROOT"
                } else {
                    "find $LARGE_TREE_ROOT -print"
                }
            val result = assertTimeoutPreemptively(Duration.ofSeconds(30)) { connection.run(command) }

            result.requireSuccess("scan huge tree")
            val lines =
                result.stdout
                    .lineSequence()
                    .filter(String::isNotBlank)
                    .toList()
            println("huge-tree-lines:${lines.size} command=$command")
            assertTrue(lines.size > 6000)
            if (python != null) {
                val sample = JsonMapper.shared().readTree(lines.first())
                assertTrue(sample.has("type"))
                assertTrue(sample.has("path"))
            }
        }
    }

    private fun openConnection(
        knownHostsFile: Path?,
        hostKeyFingerprint: String? = null,
    ): SshSftpConnection {
        val config =
            SshConnectionConfig(
                host = server.host,
                port = server.getMappedPort(SSH_PORT),
                username = SSH_USER,
                authMethod = SshAuthMethod.PASSWORD,
                password = SSH_PASSWORD,
                knownHostsFile = knownHostsFile,
                hostKeyFingerprint = hostKeyFingerprint,
            )
        return SshjSshSftpConnectionFactory().open(config)
    }

    private fun trustedKnownHosts(): Path {
        val knownHosts =
            local(
                "ssh-keyscan",
                "-t",
                "ed25519",
                "-p",
                server.getMappedPort(SSH_PORT).toString(),
                server.host,
            )
        return Files.writeString(tempDir.resolve("known_hosts"), knownHosts)
    }

    private fun knownHosts(publicKey: String): Path {
        val line = "[${server.host}]:${server.getMappedPort(SSH_PORT)} $publicKey"
        return Files.writeString(tempDir.resolve("known_hosts"), line)
    }

    private fun hostFingerprint(): String =
        local(
            "sh",
            "-c",
            "ssh-keyscan -t ed25519 -p ${server.getMappedPort(SSH_PORT)} ${server.host} 2>/dev/null " +
                "| ssh-keygen -lf - -E md5 | awk 'NR==1 {print \$2}'",
        )

    private fun impostorPublicKey(): String =
        exec(
            "sh",
            "-c",
            "rm -f /tmp/impostor* && " +
                "ssh-keygen -q -t ed25519 -N '' -f /tmp/impostor >/dev/null && " +
                "cat /tmp/impostor.pub",
        )

    private fun syncScriptHash(): String = EmbeddedScannerScript().sha256

    private class SshServer : GenericContainer<SshServer>(DockerImageName.parse(SSH_IMAGE))

    companion object {
        private val server =
            SshServer()
                .withEnv("USER_NAME", SSH_USER)
                .withEnv("USER_PASSWORD", SSH_PASSWORD)
                .withEnv("PASSWORD_ACCESS", "true")
                .withExposedPorts(SSH_PORT)
                .waitingFor(Wait.forLogMessage(READY_LOG, 1))

        @JvmStatic
        @BeforeAll
        fun startServer() = server.start()

        @JvmStatic
        @AfterAll
        fun stopServer() = server.stop()

        private fun exec(vararg command: String): String {
            val result = server.execInContainer(*command)
            check(result.exitCode == 0) { "${command.joinToString(" ")} failed: ${result.stderr}" }
            return result.stdout.trim()
        }

        private fun local(vararg command: String): String {
            val process = ProcessBuilder(*command).start()
            val stdout = process.inputStream.readAllBytes().decodeToString()
            val stderr = process.errorStream.readAllBytes().decodeToString()
            val exitCode = process.waitFor()
            check(exitCode == 0) { "${command.joinToString(" ")} failed: $stderr" }
            return stdout.trim()
        }
    }
}
