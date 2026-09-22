import com.github.spotbugs.snom.Confidence
import com.github.spotbugs.snom.Effort
import com.github.spotbugs.snom.SpotBugsTask
import dev.elide.gradle.ElideDependencyMode
import org.jetbrains.kotlin.gradle.dsl.JvmTarget

plugins {
  kotlin("jvm") version "2.4.20"
  id("org.springframework.boot") version "4.1.1"
  id("com.github.spotbugs") version "6.5.11"
  id("dev.elide")
}

group = "dev.sporesync"
version = "0.1.0"

val springBootVersion = "4.1.1"
val kotlinVersion = "2.4.20"
val sshjVersion = "0.40.0"
val jnaVersion = "5.18.1"
val spotbugsVersion = "4.10.4"
val javaVersion = 25

repositories { mavenCentral() }

kotlin {
  jvmToolchain(javaVersion)
  compilerOptions {
    jvmTarget.set(JvmTarget.JVM_25)
    allWarningsAsErrors.set(true)
    freeCompilerArgs.add("-Xjsr305=strict")
  }
}

elide {
  compiler = false
  install = false
  maven = false
  dependencyMode.set(ElideDependencyMode.GRADLE)
}

dependencies {
  implementation(platform("org.springframework.boot:spring-boot-dependencies:$springBootVersion"))
  implementation(platform("org.jetbrains.kotlin:kotlin-bom:$kotlinVersion"))
  implementation("org.springframework.boot:spring-boot-starter-webmvc")
  implementation("org.springframework.boot:spring-boot-starter-validation")
  implementation("org.springframework.boot:spring-boot-starter-data-jpa")
  implementation("org.springframework.boot:spring-boot-starter-liquibase")
  implementation("org.jetbrains.kotlin:kotlin-reflect")
  implementation("tools.jackson.module:jackson-module-kotlin")
  implementation("net.java.dev.jna:jna:$jnaVersion")
  implementation("com.hierynomus:sshj:$sshjVersion")
  runtimeOnly("org.postgresql:postgresql")
  developmentOnly("org.springframework.boot:spring-boot-devtools:$springBootVersion")
  testImplementation("org.springframework.boot:spring-boot-starter-webmvc-test")
  testImplementation("org.springframework.boot:spring-boot-testcontainers")
  testImplementation("org.testcontainers:testcontainers-junit-jupiter")
  testImplementation("org.testcontainers:testcontainers-postgresql")
  testRuntimeOnly("org.junit.platform:junit-platform-launcher")
}

dependencyLocking { lockAllConfigurations() }

springBoot { mainClass.set("dev.sporesync.ApplicationKt") }

tasks.test {
  useJUnitPlatform()
  workingDir(projectDir)
  testLogging { events("failed", "skipped") }
}

spotbugs {
  toolVersion.set(spotbugsVersion)
  effort.set(Effort.MAX)
  reportLevel.set(Confidence.MEDIUM)
  baselineFile.set(file("config/spotbugs-baseline.xml"))
}

tasks.named<SpotBugsTask>("spotbugsMain") {
  reports {
    create("xml") { required.set(true) }
    create("html") { required.set(true) }
  }
}

tasks.named<SpotBugsTask>("spotbugsTest") { enabled = false }
tasks.check { dependsOn("elideCheckFormat") }

tasks.jar { enabled = false }
tasks.bootJar { archiveFileName.set("sporesync.jar") }
