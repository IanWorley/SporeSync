using SporeSync.Domain.Model;

namespace SporeSync.Domain.Interface;

public interface IDownloadQueueItemRepository
{
    Task<PagedResult<DownloadQueueItem>> GetByRunIdAsync(
        Guid runId,
        QueueItemQuery query,
        CancellationToken cancellationToken = default);

    Task<DownloadQueueItem?> GetByIdAsync(
        Guid id,
        CancellationToken cancellationToken = default);

    // Phase 3 addition (plan:341): for future worker to load a group's internal leaves for requeue/resume.
    // Uses the Phase 2 internal SQL helper. Never exposed to UI paths (enforces no leaf leakage).
    Task<IReadOnlyList<DownloadQueueItem>> GetLeavesForGroupAsync(
        Guid runId,
        string groupRemotePath,
        CancellationToken cancellationToken = default);

    Task<DownloadQueueItem> UpsertAsync(
        UpsertDownloadQueueItem item,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Upserts all items in a single database transaction so groups and their
    /// leaves become visible (and thus claimable) atomically.
    /// </summary>
    Task<IReadOnlyList<DownloadQueueItem>> UpsertManyAsync(
        IReadOnlyCollection<UpsertDownloadQueueItem> items,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyDictionary<string, SyncedRemoteState>> GetSyncedStateAsync(
        Guid jobId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Claims the next queued item whose run has finished scanning (run status is
    /// 'downloading'), stamping a lease that expires after <paramref name="leaseSeconds"/>.
    /// </summary>
    Task<DownloadQueueItem?> ClaimNextAsync(
        int leaseSeconds,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Claims a queued internal group leaf only while its run is still downloading.
    /// Returns <c>null</c> when the run was cancelled or the leaf is no longer queued.
    /// </summary>
    Task<DownloadQueueItem?> ClaimGroupLeafAsync(
        Guid id,
        Guid runId,
        string groupRemotePath,
        int leaseSeconds,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Renews the lease of a claimed item. Returns <c>false</c> when the item is no
    /// longer in the 'downloading' status (e.g. requeued by the recovery sweep).
    /// </summary>
    Task<bool> RenewLeaseAsync(
        Guid id,
        int leaseSeconds,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns a claimed item to the queue without recording a failure (graceful
    /// shutdown/cancellation). Returns <c>null</c> when the item is not claimed.
    /// </summary>
    Task<DownloadQueueItem?> ReleaseAsync(
        Guid id,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Atomically requeues claimed items whose lease expired. Unexpired leases
    /// are always preserved so concurrent application instances cannot steal work.
    /// </summary>
    Task<IReadOnlyList<DownloadQueueItem>> RequeueStaleAsync(
        CancellationToken cancellationToken = default);

    Task<DownloadQueueItem> UpdateProgressAsync(
        UpdateDownloadQueueItemProgress update,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<DownloadQueueItem>> MarkRemoteDeletedAsync(
        Guid jobId,
        Guid syncRunId,
        IReadOnlyCollection<string> remotePaths,
        CancellationToken cancellationToken = default);

    Task<int> RequeueFailedAsync(
        Guid jobId,
        Guid syncRunId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Records a failed download attempt: increments the retry count and either requeues the item
    /// with a scheduled next attempt (backoff) or dead-letters it as terminal 'failed' once
    /// <paramref name="maxRetries"/> is exhausted.
    /// </summary>
    Task<DownloadQueueItem> RecordFailureAsync(
        Guid id,
        string? errorMessage,
        int maxRetries,
        DateTimeOffset nextAttemptAt,
        long? bytesDownloaded = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Requeues an item for a later attempt without consuming retry budget
    /// (e.g. the remote file is still inside the stability window).
    /// </summary>
    Task<DownloadQueueItem> DeferAsync(
        Guid id,
        DateTimeOffset nextAttemptAt,
        string reason,
        long? bytesDownloaded = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Manually requeues a dead-lettered ('failed') item with a fresh retry budget.
    /// Returns null when the item does not exist or is not in a terminal failed state.
    /// </summary>
    Task<DownloadQueueItem?> RetryAsync(
        Guid id,
        CancellationToken cancellationToken = default);
}
