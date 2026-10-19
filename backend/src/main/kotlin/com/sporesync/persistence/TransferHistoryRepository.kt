package com.sporesync.persistence

import org.springframework.data.jpa.repository.JpaRepository

interface TransferHistoryRepository : JpaRepository<TransferHistoryEntry, Long>
