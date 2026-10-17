package com.sporesync.config.internal

import com.sporesync.config.InvalidSettingsException
import com.sporesync.config.Settings
import com.sporesync.config.SettingsViolation
import org.springframework.boot.context.properties.source.ConfigurationPropertySource
import org.springframework.boot.env.YamlPropertySourceLoader
import org.springframework.core.env.EnumerablePropertySource
import org.springframework.core.env.MapPropertySource
import org.springframework.core.io.ByteArrayResource
import org.yaml.snakeyaml.DumperOptions
import org.yaml.snakeyaml.Yaml
import org.yaml.snakeyaml.error.MarkedYAMLException

private const val INVALID_YAML_MESSAGE = "is not valid YAML"
private const val MULTIPLE_DOCUMENTS_MESSAGE = "must contain a single YAML document"

/**
 * YAML rather than TOML: Spring Boot already parses YAML for `application.yaml` and ships SnakeYAML, so there is
 * no new dependency and the project keeps one configuration format.
 */
internal object SettingsYaml {
    private val yaml = Yaml(DumperOptions().apply { defaultFlowStyle = DumperOptions.FlowStyle.BLOCK })

    fun render(settings: Settings): String = yaml.dump(toMap(settings))

    fun parse(
        content: String,
        sourceName: String,
    ): ConfigurationPropertySource? {
        val sources =
            try {
                YamlPropertySourceLoader().load(sourceName, ByteArrayResource(content.toByteArray()))
            } catch (e: MarkedYAMLException) {
                // The parser's message quotes the offending lines, which may hold a password; keep the position only.
                val mark = e.problemMark
                val position = mark?.let { " at line ${it.line + 1}, column ${it.column + 1}" }.orEmpty()
                throw InvalidSettingsException(listOf(SettingsViolation(sourceName, INVALID_YAML_MESSAGE + position)))
            }
        if (sources.size >
            1
        ) {
            throw InvalidSettingsException(listOf(SettingsViolation(sourceName, MULTIPLE_DOCUMENTS_MESSAGE)))
        }
        val document = sources.firstOrNull() as? EnumerablePropertySource<*> ?: return null
        val prefixed =
            document.propertyNames
                .mapNotNull { name -> document.getProperty(name)?.let { "${SettingsKeys.PREFIX}.$name" to it } }
                .toMap()
        return ConfigurationPropertySource.from(MapPropertySource(sourceName, prefixed))
    }

    // Null values are left out rather than written as YAML nulls, which Spring would read back as empty strings.
    private fun toMap(settings: Settings): Map<String, Map<String, Any>> =
        with(settings) {
            mapOf(
                SettingsKeys.GENERAL to
                    mapOf(
                        SettingsKeys.DEBUG to general.debug,
                        SettingsKeys.VERBOSE to general.verbose,
                    ),
                SettingsKeys.CONNECTION to
                    listOfNotNull(
                        SettingsKeys.HOST to connection.host,
                        SettingsKeys.PORT to connection.port,
                        SettingsKeys.USERNAME to connection.username,
                        SettingsKeys.AUTH_METHOD to connection.authMethod.name,
                        connection.keyPath?.let { SettingsKeys.KEY_PATH to it.toString() },
                        connection.password?.let { SettingsKeys.PASSWORD to it },
                    ).toMap(),
                SettingsKeys.PATHS to
                    mapOf(
                        SettingsKeys.REMOTE_DIR to paths.remoteDir,
                        SettingsKeys.LOCAL_DIR to paths.localDir.toString(),
                        SettingsKeys.REMOTE_SCANNER_PATH to paths.remoteScannerPath,
                    ),
                SettingsKeys.TRANSFER to
                    listOfNotNull(
                        SettingsKeys.CONCURRENT_FILE_LIMIT to transfer.concurrentFileLimit,
                        SettingsKeys.PER_FILE_CONNECTION_LIMIT to transfer.perFileConnectionLimit,
                        transfer.bandwidthLimit?.let { SettingsKeys.BANDWIDTH_LIMIT to it.toString() },
                    ).toMap(),
                SettingsKeys.SCAN to
                    mapOf(
                        SettingsKeys.REMOTE_INTERVAL to scan.remoteInterval.toString(),
                        SettingsKeys.LOCAL_INTERVAL to scan.localInterval.toString(),
                    ),
                SettingsKeys.EXTRACTION to
                    mapOf(
                        SettingsKeys.ENABLED to extraction.enabled,
                        SettingsKeys.DELETE_ARCHIVE_AFTER_EXTRACT to extraction.deleteArchiveAfterExtract,
                    ),
                SettingsKeys.AUTOQUEUE to
                    mapOf(
                        SettingsKeys.ENABLED to autoqueue.enabled,
                        SettingsKeys.PATTERNS_ONLY to autoqueue.patternsOnly,
                    ),
            )
        }
}
