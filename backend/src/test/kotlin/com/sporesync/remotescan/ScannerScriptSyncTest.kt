package com.sporesync.remotescan

import org.junit.jupiter.api.Assertions.assertArrayEquals
import org.junit.jupiter.api.Assertions.assertEquals
import org.junit.jupiter.api.Assertions.assertFalse
import org.junit.jupiter.api.Assertions.assertNull
import org.junit.jupiter.api.Assertions.assertTrue
import org.junit.jupiter.api.Test

private const val REMOTE_SCANNER_PATH = "/tmp/.sporesync/scanner.py"

class ScannerScriptSyncTest {
    private val embeddedScript = EmbeddedScannerScript()

    @Test
    fun `uploads script when remote file is missing`() {
        val connection = FakeSshSftpConnection()

        val result = ScannerScriptSync(embeddedScript).ensureCurrent(connection, REMOTE_SCANNER_PATH)

        assertTrue(result.uploaded)
        assertNull(result.remoteHashBeforeUpload)
        assertEquals(embeddedScript.sha256, result.localHash)
        assertEquals(listOf("mkdir -p '/tmp/.sporesync'"), connection.commands)
        assertArrayEquals(embeddedScript.bytes, connection.files[REMOTE_SCANNER_PATH])
    }

    @Test
    fun `does not upload script when remote hash already matches`() {
        val connection =
            FakeSshSftpConnection(
                files = mutableMapOf(REMOTE_SCANNER_PATH to embeddedScript.bytes.copyOf()),
            )

        val result = ScannerScriptSync(embeddedScript).ensureCurrent(connection, REMOTE_SCANNER_PATH)

        assertFalse(result.uploaded)
        assertEquals(embeddedScript.sha256, result.localHash)
        assertEquals(embeddedScript.sha256, result.remoteHashBeforeUpload)
        assertEquals(listOf("mkdir -p '/tmp/.sporesync'"), connection.commands)
    }

    @Test
    fun `replaces stale script when remote hash differs`() {
        val stale = "print('stale')\n".toByteArray()
        val connection = FakeSshSftpConnection(files = mutableMapOf(REMOTE_SCANNER_PATH to stale))

        val result = ScannerScriptSync(embeddedScript).ensureCurrent(connection, REMOTE_SCANNER_PATH)

        assertTrue(result.uploaded)
        assertEquals(stale.sha256Hex(), result.remoteHashBeforeUpload)
        assertArrayEquals(embeddedScript.bytes, connection.files[REMOTE_SCANNER_PATH])
    }
}

private class FakeSshSftpConnection(
    val files: MutableMap<String, ByteArray> = mutableMapOf(),
) : SshSftpConnection {
    val commands = mutableListOf<String>()

    override fun run(command: String): SshCommandResult {
        commands += command
        return SshCommandResult(exitCode = 0, stdout = "", stderr = "")
    }

    override fun readFile(path: String): ByteArray? = files[path]?.copyOf()

    override fun writeFile(
        path: String,
        content: ByteArray,
    ) {
        files[path] = content.copyOf()
    }

    override fun close() = Unit
}
