package dev.sporesync.settings

import jakarta.persistence.Column
import jakarta.persistence.Entity
import jakarta.persistence.Id
import jakarta.persistence.Table

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

  constructor(name: String, value: String) : this() {
    this.name = name
    this.value = value
  }
}
