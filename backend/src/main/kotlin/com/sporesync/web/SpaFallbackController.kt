package com.sporesync.web

import org.springframework.beans.factory.annotation.Value
import org.springframework.context.annotation.Configuration
import org.springframework.core.io.Resource
import org.springframework.web.servlet.config.annotation.ResourceHandlerRegistry
import org.springframework.web.servlet.config.annotation.ViewControllerRegistry
import org.springframework.web.servlet.config.annotation.WebMvcConfigurer
import org.springframework.web.servlet.resource.PathResourceResolver
import java.nio.file.Files
import java.nio.file.Path

private const val FRONTEND_DIST_PROPERTY = "sporesync.frontend.dist"
private const val FRONTEND_DIST_ENV = "SPORESYNC_FRONTEND_DIST"
private const val FRONTEND_DIST_INDEX = "index.html"
private const val FRONTEND_DIST_RELATIVE = "frontend/dist"

@Configuration
class SpaFallbackController(
    @Value("\${$FRONTEND_DIST_PROPERTY:}") configuredDist: String,
) : WebMvcConfigurer {
    private val frontendDist = resolveFrontendDist(configuredDist)

    override fun addViewControllers(registry: ViewControllerRegistry) {
        registry.addViewController("/").setViewName("forward:/$FRONTEND_DIST_INDEX")
    }

    override fun addResourceHandlers(registry: ResourceHandlerRegistry) {
        registry
            .addResourceHandler("/**")
            .addResourceLocations(frontendDist.toUri().toString())
            .resourceChain(false)
            .addResolver(SpaFallbackResourceResolver())
    }

    private fun resolveFrontendDist(configuredDist: String): Path {
        if (configuredDist.isNotBlank()) {
            val configuredPath = Path.of(configuredDist).toAbsolutePath().normalize()
            return requireFrontendDist(configuredPath)
        }
        val codeSource = Path.of(SpaFallbackController::class.java.protectionDomain.codeSource.location.toURI())
        val start = if (Files.isRegularFile(codeSource)) codeSource.parent else codeSource
        val resolved =
            generateSequence(start.toAbsolutePath().normalize()) { it.parent }
                .map { it.resolve(FRONTEND_DIST_RELATIVE) }
                .firstOrNull(::hasFrontendIndex)
        if (resolved != null) return resolved
        throw missingFrontendDistError("unable to locate frontend/dist from application code location")
    }

    private fun requireFrontendDist(path: Path): Path {
        if (hasFrontendIndex(path)) return path
        throw missingFrontendDistError("missing $FRONTEND_DIST_INDEX at $path")
    }

    private fun hasFrontendIndex(path: Path): Boolean = Files.isRegularFile(path.resolve(FRONTEND_DIST_INDEX))

    private fun missingFrontendDistError(reason: String): IllegalStateException =
        IllegalStateException(
            "Frontend bundle is missing ($reason). Build it with `cd frontend && vp build`, or set " +
                "$FRONTEND_DIST_ENV to an absolute dist directory.",
        )
}

private class SpaFallbackResourceResolver : PathResourceResolver() {
    override fun getResource(resourcePath: String, location: Resource): Resource? {
        val staticResource = super.getResource(resourcePath, location)
        if (staticResource != null) return staticResource
        if (!shouldFallbackToIndex(resourcePath)) return null
        return super.getResource(FRONTEND_DIST_INDEX, location)
    }

    private fun shouldFallbackToIndex(resourcePath: String): Boolean {
        val normalized = resourcePath.trimStart('/')
        if (normalized.isBlank()) return true
        if (normalized == "api" || normalized.startsWith("api/")) return false
        if (normalized == "actuator" || normalized.startsWith("actuator/")) return false
        return !normalized.substringAfterLast('/').contains('.')
    }
}
