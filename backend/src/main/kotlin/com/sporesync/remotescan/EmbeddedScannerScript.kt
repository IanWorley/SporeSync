package com.sporesync.remotescan

import org.springframework.stereotype.Component
import java.security.MessageDigest
import java.util.HexFormat

private const val RESOURCE_PATH = "/remotescan/scanner.py"
private const val HASH_ALGORITHM = "SHA-256"

@Component
class EmbeddedScannerScript {
    val bytes: ByteArray =
        checkNotNull(this::class.java.getResourceAsStream(RESOURCE_PATH)) { "Missing resource $RESOURCE_PATH" }
            .use { it.readAllBytes() }

    val sha256: String = bytes.sha256Hex()
}

internal fun ByteArray.sha256Hex(): String {
    val digest = MessageDigest.getInstance(HASH_ALGORITHM).digest(this)
    return HexFormat.of().formatHex(digest)
}
