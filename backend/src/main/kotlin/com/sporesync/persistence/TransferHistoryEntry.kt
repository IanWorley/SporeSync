package com.sporesync.persistence

import jakarta.persistence.Column
import jakarta.persistence.Entity
import jakarta.persistence.EnumType
import jakarta.persistence.Enumerated
import jakarta.persistence.GeneratedValue
import jakarta.persistence.GenerationType
import jakarta.persistence.Id
import jakarta.persistence.Table
import java.time.Instant

@Entity
@Table(name = "transfer_history")
class TransferHistoryEntry private constructor(
    @Column(nullable = false)
    val path: String,
    @Enumerated(EnumType.STRING)
    @Column(nullable = false)
    val outcome: TransferOutcome,
    @Column(name = "finished_at", nullable = false)
    val finishedAt: Instant,
    @Column(name = "failure_reason")
    val failureReason: String?,
) {
    @Id
    @GeneratedValue(strategy = GenerationType.IDENTITY)
    val id: Long? = null

    companion object {
        fun completed(
            path: String,
            finishedAt: Instant,
        ): TransferHistoryEntry = TransferHistoryEntry(path, TransferOutcome.COMPLETED, finishedAt, null)

        fun failed(
            path: String,
            finishedAt: Instant,
            reason: String,
        ): TransferHistoryEntry = TransferHistoryEntry(path, TransferOutcome.FAILED, finishedAt, reason)
    }
}
