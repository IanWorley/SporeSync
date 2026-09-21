package dev.sporesync.config

import java.nio.file.Files
import java.nio.file.Path
import org.springframework.boot.context.properties.ConfigurationProperties
import org.springframework.stereotype.Component

// Deliberately not a data class: generated toString must not expose credentials.
@Component
@ConfigurationProperties("sporesync.ssh")
class SshSettings {
  var privateKey: String = ""
  var passphrase: String = ""
  var knownHosts: String = ""
  var scanner: String = "../scanner/inventory.py"

  fun validate() {
    require(
        listOf(privateKey, knownHosts, scanner).all {
          it.isNotBlank() && Files.isRegularFile(Path.of(it))
        }
    )
  }
}
