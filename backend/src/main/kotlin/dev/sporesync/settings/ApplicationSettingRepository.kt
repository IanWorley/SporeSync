package dev.sporesync.settings

import org.springframework.data.jpa.repository.JpaRepository

interface ApplicationSettingRepository : JpaRepository<ApplicationSetting, String>
