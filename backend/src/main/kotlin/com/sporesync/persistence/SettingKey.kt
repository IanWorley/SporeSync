package com.sporesync.persistence

import jakarta.persistence.Column
import jakarta.persistence.Embeddable
import java.io.Serializable

/** Identifies one setting the same way the config API does: `/api/config/{section}/{key}`. */
@Embeddable
data class SettingKey(
    @Column(nullable = false)
    val section: String,
    @Column(nullable = false)
    val name: String,
) : Serializable {
    // JPA requires composite ids to be Serializable.
    companion object {
        private const val serialVersionUID: Long = 1L
    }
}
