package com.sporesync.config.internal

import com.sporesync.config.InvalidSettingsException
import com.sporesync.config.Settings
import com.sporesync.config.SettingsViolation
import org.springframework.boot.context.properties.bind.BindException
import org.springframework.boot.context.properties.bind.BindHandler
import org.springframework.boot.context.properties.bind.Bindable
import org.springframework.boot.context.properties.bind.Binder
import org.springframework.boot.context.properties.bind.UnboundConfigurationPropertiesException
import org.springframework.boot.context.properties.bind.handler.NoUnboundElementsBindHandler
import org.springframework.boot.context.properties.source.ConfigurationPropertyName
import org.springframework.boot.context.properties.source.ConfigurationPropertySource

private const val PREFIX_WITH_DOT = SettingsKeys.PREFIX + "."
private const val UNKNOWN_SETTING_MESSAGE = "is not a recognized setting"

/** Binds [Settings] from property sources with Spring's relaxed binding. */
internal object SettingsBinder {
    /**
     * Binds the settings [file] with [overrides] taking precedence. Unknown keys in the file, such as a misspelt
     * `connection.prot`, are rejected. Overrides are not checked: the environment holds many unrelated properties.
     */
    fun bind(
        file: ConfigurationPropertySource?,
        overrides: List<ConfigurationPropertySource> = emptyList(),
    ): Settings {
        val rejectUnknownFileKeys = NoUnboundElementsBindHandler(BindHandler.DEFAULT) { it === file }
        return try {
            Binder(overrides + listOfNotNull(file))
                .bindOrCreate(SettingsKeys.PREFIX, Bindable.of(Settings::class.java), rejectUnknownFileKeys)
        } catch (e: BindException) {
            throw InvalidSettingsException(violations(e))
        }
    }

    // Causes quote the rejected values, so they are dropped rather than chained: a value may be a secret.
    private fun violations(e: BindException): List<SettingsViolation> {
        val unbound = e.cause as? UnboundConfigurationPropertiesException
        if (unbound != null) {
            return unbound.unboundProperties.map {
                val message = if (it.value?.toString().isNullOrBlank()) BLANK_MESSAGE else UNKNOWN_SETTING_MESSAGE
                SettingsViolation(fieldName(it.name), message)
            }
        }
        val type =
            e.target.type
                .resolve()
                ?.simpleName
        return listOf(SettingsViolation(fieldName(e.name), "is not a valid $type"))
    }

    private fun fieldName(name: ConfigurationPropertyName): String = name.toString().removePrefix(PREFIX_WITH_DOT)
}
