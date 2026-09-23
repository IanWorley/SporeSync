package dev.sporesync.ssh

import java.nio.file.Files
import java.nio.file.Path
import net.schmizz.sshj.SSHClient
import org.springframework.boot.context.properties.ConfigurationProperties
import org.springframework.stereotype.Component

enum class SshAuthentication {
  KEY,
  PASSWORD,
}

// Deliberately not a data class: generated toString must not expose credentials.
@Component
@ConfigurationProperties("sporesync.ssh")
class SshSettings {
  var authentication: SshAuthentication = SshAuthentication.KEY
  var password: String = ""
  var privateKey: String = ""
  var passphrase: String = ""
  var knownHosts: String = ""
  var scanner: String = "../scanner/inventory.py"

  fun validate() {
    require(
        listOf(knownHosts, scanner).all {
          it.isNotBlank() && Files.isRegularFile(Path.of(it))
        }
    )
    when (authentication) {
      SshAuthentication.KEY ->
          require(privateKey.isNotBlank() && Files.isRegularFile(Path.of(privateKey)))
      SshAuthentication.PASSWORD -> require(password.isNotEmpty())
    }
  }

  // Load key material before connecting so local key failures remain configuration errors.
  fun prepareAuthentication(ssh: SSHClient, username: String): () -> Unit =
      when (authentication) {
        SshAuthentication.KEY -> {
          val key = ssh.loadKeys(privateKey, passphrase)
          val authenticate: () -> Unit = { ssh.authPublickey(username, key) }
          authenticate
        }
        SshAuthentication.PASSWORD -> {
          val secret = password
          { ssh.authPassword(username, secret) }
        }
      }
}
