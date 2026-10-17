package com.sporesync.persistence

import org.springframework.data.jpa.repository.JpaRepository

interface SettingsCacheRepository : JpaRepository<SettingsCacheEntry, SettingKey>
