package com.sporesync.persistence

import jakarta.persistence.Column
import jakarta.persistence.Embeddable
import java.io.Serializable

@Embeddable
data class SettingId(
    @Column(nullable = false)
    val section: String,
    // KEY is a reserved word in H2, so the column carries a prefix.
    @Column(name = "setting_key", nullable = false)
    val key: String,
) : Serializable {
    // JPA requires composite ids to be Serializable.
    companion object {
        private const val serialVersionUID: Long = 1L
    }
}
