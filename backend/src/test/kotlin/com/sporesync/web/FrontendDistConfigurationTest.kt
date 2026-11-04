package com.sporesync.web

import com.sporesync.SporeSyncApplication
import org.junit.jupiter.api.Assertions.assertTrue
import org.junit.jupiter.api.Test
import org.junit.jupiter.api.assertThrows
import org.springframework.boot.builder.SpringApplicationBuilder
import java.nio.file.Path
import java.util.UUID

class FrontendDistConfigurationTest {
    @Test
    fun `application fails fast when frontend dist is missing`() {
        val missingDist = Path.of("/tmp/sporesync-missing-dist-${UUID.randomUUID()}")
        val thrown =
            assertThrows<Exception> {
                SpringApplicationBuilder(SporeSyncApplication::class.java)
                    .properties(
                        "server.port=0",
                        "logging.level.root=OFF",
                        "spring.main.banner-mode=off",
                        "sporesync.frontend.dist=$missingDist",
                    ).run()
            }

        val message = rootCause(thrown).message.orEmpty()
        assertTrue(message.contains("Frontend bundle is missing"))
        assertTrue(message.contains(missingDist.toString()))
    }

    private fun rootCause(throwable: Throwable): Throwable {
        var current = throwable
        while (current.cause != null && current.cause !== current) {
            current = current.cause!!
        }
        return current
    }
}
