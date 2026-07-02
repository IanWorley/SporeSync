using SporeSync.Business.Interface;
using SporeSync.Domain.Interface;
using SporeSync.Domain.Model;

namespace SporeSync.Business.Service;

public sealed class SyncRunControlService : ISyncRunControlService
{
    private readonly ISporeSyncRunRepository _runRepository;
    private readonly ISyncDashboardNotifier _notifier;

    public SyncRunControlService(
        ISporeSyncRunRepository runRepository,
        ISyncDashboardNotifier notifier)
    {
        _runRepository = runRepository;
        _notifier = notifier;
    }

    public async Task<SyncRunControlResult> CancelRunAsync(
        Guid runId,
        CancellationToken cancellationToken = default)
    {
        var run = await _runRepository.GetByIdAsync(runId, cancellationToken);
        if (run is null)
        {
            return new SyncRunControlResult { Error = SyncRunControlError.NotFound };
        }

        var cancelled = await _runRepository.CancelAsync(runId, cancellationToken);
        if (cancelled is null)
        {
            return new SyncRunControlResult { Error = SyncRunControlError.NotActive };
        }

        // Pending items were just marked skipped; settle the run's aggregate counts.
        cancelled = await _runRepository.RecalculateAggregatesAsync(runId, cancellationToken);
        await _notifier.NotifyRunUpdatedAsync(cancelled, cancellationToken);

        return new SyncRunControlResult { Run = cancelled };
    }

    public async Task<SyncRunControlResult> RetryFailedItemsAsync(
        Guid runId,
        CancellationToken cancellationToken = default)
    {
        var run = await _runRepository.GetByIdAsync(runId, cancellationToken);
        if (run is null)
        {
            return new SyncRunControlResult { Error = SyncRunControlError.NotFound };
        }

        var retriedCount = await _runRepository.RetryFailedItemsAsync(runId, cancellationToken);
        if (retriedCount == 0)
        {
            return new SyncRunControlResult { Error = SyncRunControlError.NoFailedItems };
        }

        var updated = await _runRepository.RecalculateAggregatesAsync(runId, cancellationToken);
        await _notifier.NotifyRunUpdatedAsync(updated, cancellationToken);

        return new SyncRunControlResult { Run = updated, RetriedCount = retriedCount };
    }
}
