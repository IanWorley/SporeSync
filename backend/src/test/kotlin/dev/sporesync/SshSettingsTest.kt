package dev.sporesync

import dev.sporesync.ssh.SshAuthentication
import dev.sporesync.ssh.SshSettings
import org.junit.jupiter.api.Assertions.assertEquals
import org.junit.jupiter.api.Assertions.assertThrows
import org.junit.jupiter.api.Test
import org.springframework.boot.context.properties.bind.BindException
import org.springframework.boot.context.properties.bind.Binder
import org.springframework.boot.context.properties.source.ConfigurationPropertySources
import org.springframework.core.env.StandardEnvironment.SYSTEM_ENVIRONMENT_PROPERTY_SOURCE_NAME
import org.springframework.core.env.SystemEnvironmentPropertySource

class SshSettingsTest {
  @Test
  fun `existing key environment configuration defaults to key authentication`() {
    val settings = bind(mapOf("SPORESYNC_SSH_PRIVATEKEY" to "/configured/key"))
    assertEquals(SshAuthentication.KEY, settings.authentication)
    assertEquals("/configured/key", settings.privateKey)
  }

  @Test
  fun `password environment configuration preserves password whitespace`() {
    val settings =
        bind(
            mapOf(
                "SPORESYNC_SSH_AUTHENTICATION" to "PASSWORD",
                "SPORESYNC_SSH_PASSWORD" to " test-only password ",
            )
        )
    assertEquals(SshAuthentication.PASSWORD, settings.authentication)
    assertEquals(" test-only password ", settings.password)
    assertEquals("", settings.privateKey)
  }

  @Test
  fun `unknown authentication method fails binding`() {
    assertThrows(BindException::class.java) {
      bind(mapOf("SPORESYNC_SSH_AUTHENTICATION" to "unsupported"))
    }
  }

  private fun bind(values: Map<String, Any>): SshSettings =
      Binder(
              ConfigurationPropertySources.from(
                      SystemEnvironmentPropertySource(
                          SYSTEM_ENVIRONMENT_PROPERTY_SOURCE_NAME,
                          values,
                      )
                  )
                  .map { requireNotNull(it) }
          )
          .bind("sporesync.ssh", SshSettings::class.java)
          .get()
}
