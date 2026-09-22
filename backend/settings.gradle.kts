pluginManagement {
    val elidePluginCommit = "a1bb1307203acb44fa0d622aad4870c69f1144e1"
    val elidePluginSource = file(".dev/elide-gradle")
    val elidePluginMarker = elidePluginSource.resolve(".sporesync-plugin-commit")
    check(elidePluginSource.isDirectory &&
        elidePluginMarker.isFile &&
        elidePluginMarker.readText().trim() == elidePluginCommit) {
        "Elide Gradle plugin source is missing at $elidePluginSource. " +
            "Run bash scripts/prepare-elide-plugin.sh from backend/."
    }
    includeBuild(".dev/elide-gradle")
}

plugins {
    id("dev.elide.settings")
}

rootProject.name = "sporesync-backend"

elide {
    runtime {
        mode = dev.elide.gradle.ElideRuntimeMode.MANAGED
        version = "1.5.3+20260917"
    }
}
