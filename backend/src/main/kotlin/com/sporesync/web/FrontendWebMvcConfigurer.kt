package com.sporesync.web

import org.slf4j.LoggerFactory
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
private const val FRONTEND_REQUIRED_PROPERTY = "sporesync.frontend.required"
private const val FRONTEND_DIST_ENV = "SPORESYNC_FRONTEND_DIST"
private const val FRONTEND_DIST_INDEX = "index.html"
private const val FRONTEND_DIST_DEFAULT = "../frontend/dist"

@Configuration
class FrontendWebMvcConfigurer(
    @Value("\${$FRONTEND_DIST_PROPERTY:$FRONTEND_DIST_DEFAULT}") configuredDist: String,
    @Value("\${$FRONTEND_REQUIRED_PROPERTY:false}") frontendRequired: Boolean,
) : WebMvcConfigurer {
    private val logger = LoggerFactory.getLogger(javaClass)
    private val frontendDist = resolveFrontendDist(configuredDist, frontendRequired)

    override fun addViewControllers(registry: ViewControllerRegistry) {
        if (frontendDist == null) return
        registry.addViewController("/").setViewName("forward:/$FRONTEND_DIST_INDEX")
    }

    override fun addResourceHandlers(registry: ResourceHandlerRegistry) {
        if (frontendDist == null) return
        registry
            .addResourceHandler("/**")
            .addResourceLocations(frontendDist.toUri().toString())
            .resourceChain(false)
            .addResolver(FrontendSpaFallbackResourceResolver())
    }

    private fun resolveFrontendDist(configuredDist: String, frontendRequired: Boolean): Path? {
        val frontendPath = Path.of(configuredDist).toAbsolutePath().normalize()
        if (Files.isRegularFile(frontendPath.resolve(FRONTEND_DIST_INDEX))) return frontendPath
        val message =
            "Frontend bundle is missing ($FRONTEND_DIST_INDEX at $frontendPath). Build it with " +
                "`cd frontend && vp build`, or set $FRONTEND_DIST_ENV."
        if (frontendRequired) throw IllegalStateException(message)
        logger.warn("$message Frontend routes are disabled because $FRONTEND_REQUIRED_PROPERTY=false.")
        return null
    }
}

private class FrontendSpaFallbackResourceResolver : PathResourceResolver() {
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
