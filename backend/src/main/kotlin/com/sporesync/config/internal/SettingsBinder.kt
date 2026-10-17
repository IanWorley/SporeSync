package com.sporesync.config.internal

import com.sporesync.config.InvalidSettingsException
import com.sporesync.config.Settings
import com.sporesync.config.SettingsViolation
import org.springframework.boot.context.properties.bind.BindException
import org.springframework.boot.context.properties.bind.Bindable
import org.springframework.boot.context.properties.bind.Binder
import org.springframework.boot.context.properties.source.ConfigurationPropertySource

private const val PREFIX_WITH_DOT = SettingsKeys.PREFIX + "."

/** Binds [Settings] from property sources with Spring's relaxed binding. Earlier sources win. */
internal object SettingsBinder {
    fun bind(sources: List<ConfigurationPropertySource>): Settings =
        try {
            Binder(sources).bindOrCreate(SettingsKeys.PREFIX, Bindable.of(Settings::class.java))
        } catch (e: BindException) {
            // The cause quotes the rejected value, so it is dropped rather than chained: the value may be a secret.
            val field = e.name.toString().removePrefix(PREFIX_WITH_DOT)
            val type =
                e.target.type
                    .resolve()
                    ?.simpleName
            throw InvalidSettingsException(listOf(SettingsViolation(field, "is not a valid $type")))
        }
}
