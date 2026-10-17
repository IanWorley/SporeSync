package com.sporesync

import org.springframework.boot.autoconfigure.SpringBootApplication
import org.springframework.boot.runApplication

@SpringBootApplication
class SporeSyncApplication

// Copying args once at startup is harmless; this is the standard Spring Boot entry point.
@Suppress("SpreadOperator")
fun main(args: Array<String>) {
    runApplication<SporeSyncApplication>(*args)
}
