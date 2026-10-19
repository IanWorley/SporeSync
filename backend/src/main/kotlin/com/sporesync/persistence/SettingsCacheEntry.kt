package com.sporesync.persistence

import jakarta.persistence.Column
import jakarta.persistence.EmbeddedId
import jakarta.persistence.Entity
import jakarta.persistence.Table

@Entity
@Table(name = "settings_cache")
class SettingsCacheEntry(
    @EmbeddedId
    val id: SettingId,
    // VALUE is a reserved word in H2, so the column carries a prefix.
    @Column(name = "setting_value", nullable = false)
    var value: String,
)
