package com.sporesync

import org.junit.jupiter.api.Assertions.assertEquals
import org.junit.jupiter.api.Test
import org.springframework.modulith.core.ApplicationModules
import org.springframework.modulith.docs.Documenter
import org.springframework.modulith.docs.Documenter.CanvasOptions
import org.springframework.modulith.docs.Documenter.DiagramOptions
import org.springframework.modulith.docs.Documenter.DiagramOptions.ElementsWithoutRelationships
import java.nio.file.Path
import kotlin.io.path.readText

// Elide keeps each member's build output in `.dev/`. The test working directory is wherever `elide test`
// was started, so the docs folder is resolved from the compiled classes instead of the working directory.
private const val BUILD_OUTPUT_DIR = ".dev"
private const val DOCS_DIR = "spring-modulith-docs"
private const val OVERVIEW_DIAGRAM = "components.puml"
private val DIAGRAM_COMPONENT_NAME = Regex("""Component\([^,]+, "(\w+)"""")

private val SPEC_MODULES =
    setOf(
        "autoqueue",
        "config",
        "delete",
        "extract",
        "filestate",
        "localscan",
        "model",
        "persistence",
        "remotescan",
        "transfer",
        "web",
    )

class ModularityTest {
    private val modules = ApplicationModules.of(SporeSyncApplication::class.java)

    @Test
    fun `modules only depend on each other's exported API`() {
        modules.verify()
    }

    @Test
    fun `overview diagram shows every module`() {
        val docsFolder = docsFolder()
        val diagramOptions =
            DiagramOptions.defaults().withElementsWithoutRelationships(ElementsWithoutRelationships.VISIBLE)

        // Skips writeModuleMetadata(), which writes into a working-directory-relative Gradle/Maven path.
        Documenter(modules, Documenter.Options.defaults().withOutputFolder(docsFolder.toString()))
            .writeModulesAsPlantUml(diagramOptions)
            .writeIndividualModulesAsPlantUml(diagramOptions)
            .writeModuleCanvases()
            .writeAggregatingDocument(diagramOptions, CanvasOptions.defaults())

        val diagram = docsFolder.resolve(OVERVIEW_DIAGRAM).readText()
        val diagrammedModules =
            DIAGRAM_COMPONENT_NAME.findAll(diagram).map { it.groupValues[1].lowercase() }.toSet()
        assertEquals(SPEC_MODULES, diagrammedModules)
    }

    private fun docsFolder(): Path {
        val codeSource = ModularityTest::class.java.protectionDomain.codeSource
        val classesDir = Path.of(codeSource.location.toURI())
        val buildOutput =
            generateSequence(classesDir) { it.parent }
                .firstOrNull { it.fileName?.toString() == BUILD_OUTPUT_DIR }
                ?: error("Test classes at $classesDir are not under a $BUILD_OUTPUT_DIR build directory")
        return buildOutput.resolve(DOCS_DIR)
    }
}
