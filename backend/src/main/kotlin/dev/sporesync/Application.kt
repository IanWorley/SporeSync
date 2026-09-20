package dev.sporesync

import org.springframework.boot.autoconfigure.SpringBootApplication
import org.springframework.boot.runApplication

// Avoid requiring the Kotlin all-open compiler plugin for this configuration.
@SpringBootApplication(proxyBeanMethods = false) class Application

fun main(args: Array<String>) {
  runApplication<Application>(*args)
}
