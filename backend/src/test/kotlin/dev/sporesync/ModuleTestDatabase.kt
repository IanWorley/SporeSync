package dev.sporesync

import org.springframework.boot.test.context.TestConfiguration
import org.springframework.boot.testcontainers.service.connection.ServiceConnection
import org.springframework.context.annotation.Bean
import org.testcontainers.postgresql.PostgreSQLContainer

@TestConfiguration(proxyBeanMethods = false)
class ModuleTestDatabase {
  @Bean @ServiceConnection fun postgres(): PostgreSQLContainer = PostgreSQLContainer(POSTGRES_IMAGE)

  private companion object {
    const val POSTGRES_IMAGE = "postgres:17.6-alpine"
  }
}
