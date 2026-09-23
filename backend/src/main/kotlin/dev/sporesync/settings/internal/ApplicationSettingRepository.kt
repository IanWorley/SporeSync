package dev.sporesync.settings.internal

import org.springframework.data.jpa.repository.JpaRepository

interface ApplicationSettingRepository : JpaRepository<ApplicationSetting, String>
