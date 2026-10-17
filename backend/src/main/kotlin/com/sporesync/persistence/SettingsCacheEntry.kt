package com.sporesync.persistence

import jakarta.persistence.Column
import jakarta.persistence.EmbeddedId
import jakarta.persistence.Entity
import jakarta.persistence.Table

/**
 * Last known value of one setting. The settings file owned by `config` stays the source of truth;
 * this cache only holds a copy.
 */
@Entity
@Table(name = "settings_cache")
class SettingsCacheEntry(
    @EmbeddedId
    val key: SettingKey,
    // VALUE is a reserved word in H2, so the column carries a prefix.
    @Column(name = "setting_value", nullable = false)
    var value: String,
)
