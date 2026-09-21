package dev.sporesync.model.settings

import jakarta.persistence.Column
import jakarta.persistence.Entity
import jakarta.persistence.Id
import jakarta.persistence.Table
import java.time.Instant
import org.hibernate.annotations.CreationTimestamp
import org.hibernate.annotations.SourceType
import org.hibernate.annotations.UpdateTimestamp

@Entity
@Table(name = "sporesync_settings")
open class ApplicationSetting protected constructor() {
  @field:Id
  @field:Column(name = "name", nullable = false, columnDefinition = "text")
  open lateinit var name: String
    protected set

  @field:Column(name = "value", nullable = false, columnDefinition = "text")
  open lateinit var value: String
    protected set

  @field:CreationTimestamp(source = SourceType.DB)
  @field:Column(name = "created_at", nullable = false, updatable = false)
  open lateinit var createdAt: Instant
    protected set

  @field:UpdateTimestamp(source = SourceType.DB)
  @field:Column(name = "updated_at", nullable = false)
  open lateinit var updatedAt: Instant
    protected set

  constructor(name: String, value: String) : this() {
    this.name = name
    this.value = value
  }
}
