using SporeSync.Business.Scanning;
using SporeSync.Domain.Model;

namespace SporeSync.Business.Worker;

public interface IChangeDetector
{
    ChangeDetectionResult DetectChanges(
        FirstLevelScanResult scanResult,
        IReadOnlyDictionary<string, SyncedRemoteState> syncedState);
}

public sealed record ChangeDetectionResult(
    IReadOnlyList<ScannedRemoteEntry> EntriesToEnqueue,
    IReadOnlyList<ScannedRemoteEntry> EntriesToCarryForward,
    IReadOnlyList<string> RemoteDeletedPaths,
    int EnqueuedVisibleCount,
    long EnqueuedTotalBytes);

/// <summary>
/// Diffs a fresh scan against the last persisted state at leaf granularity.
/// Groups are re-enqueued when any individual leaf changed (or the group's own
/// aggregates drifted, e.g. a leaf was removed remotely), but unchanged completed
/// leaves are carried forward into the new run instead of being re-downloaded.
/// </summary>
public sealed class ChangeDetector : IChangeDetector
{
    public ChangeDetectionResult DetectChanges(
        FirstLevelScanResult scanResult,
        IReadOnlyDictionary<string, SyncedRemoteState> syncedState)
    {
        var toEnqueue = new List<ScannedRemoteEntry>();
        var toCarryForward = new List<ScannedRemoteEntry>();
        var scannedRemotePaths = scanResult.VisibleEntries
            .Concat(scanResult.InternalLeafEntries)
            .Select(entry => entry.RemotePath)
            .ToHashSet(StringComparer.Ordinal);
        var remoteDeletedPaths = syncedState.Values
            .Where(state => IsRemoteDeletedCandidate(state.Status) && !scannedRemotePaths.Contains(state.RemotePath))
            .Select(state => state.RemotePath)
            .ToArray();
        var leavesByGroup = scanResult.InternalLeafEntries
            .Where(leaf => leaf.GroupRemotePath is not null)
            .GroupBy(leaf => leaf.GroupRemotePath, StringComparer.Ordinal)
            .ToDictionary(group => group.Key!, group => group.ToArray(), StringComparer.Ordinal);

        var enqueuedVisibleCount = 0;
        long enqueuedTotalBytes = 0;

        foreach (var visible in scanResult.VisibleEntries)
        {
            if (!visible.IsGroup)
            {
                if (RequiresDownload(visible, syncedState))
                {
                    toEnqueue.Add(visible);
                    enqueuedVisibleCount++;
                    enqueuedTotalBytes += visible.FileSizeBytes;
                }

                continue;
            }

            var leaves = leavesByGroup.TryGetValue(visible.RemotePath, out var groupLeaves)
                ? groupLeaves
                : Array.Empty<ScannedRemoteEntry>();
            var changedLeaves = leaves
                .Where(leaf => RequiresDownload(leaf, syncedState))
                .ToArray();

            if (changedLeaves.Length == 0 && !GroupMetadataChanged(visible, syncedState))
            {
                continue;
            }

            // Leaves are enqueued before the group row so the download worker can
            // never claim the group while its leaf rows are still being upserted.
            toEnqueue.AddRange(changedLeaves);
            toEnqueue.Add(visible);
            toCarryForward.AddRange(leaves.Except(changedLeaves));
            enqueuedVisibleCount++;
            enqueuedTotalBytes += visible.FileSizeBytes;
        }

        return new ChangeDetectionResult(
            toEnqueue,
            toCarryForward,
            remoteDeletedPaths,
            enqueuedVisibleCount,
            enqueuedTotalBytes);
    }

    private static bool RequiresDownload(
        ScannedRemoteEntry entry,
        IReadOnlyDictionary<string, SyncedRemoteState> syncedState)
    {
        if (!syncedState.TryGetValue(entry.RemotePath, out var existing))
        {
            return true;
        }

        var remoteChanged = existing.RemoteModifiedAt != entry.RemoteModifiedAt
            || existing.FileSizeBytes != entry.FileSizeBytes;
        if (remoteChanged)
        {
            // New remote content: re-enqueue (the upsert resets progress and the retry budget).
            return true;
        }

        if (IsCompleted(existing.Status))
        {
            return !File.Exists(entry.DestinationPath);
        }

        // Remote unchanged and not completed:
        // - 'failed' items are dead-lettered (retry budget exhausted); re-enqueueing them here
        //   would create an infinite retry loop across scans. They are revived only by a remote
        //   change or an explicit manual retry.
        // - 'queued'/'comparing'/'downloading' items are in flight or awaiting a scheduled retry;
        //   re-upserting them would reset their progress and retry budget mid-cycle.
        // - 'skipped' items (e.g. previously remote-deleted) that reappear in the scan should be
        //   downloaded again.
        return string.Equals(existing.Status, "skipped", StringComparison.OrdinalIgnoreCase);
    }

    private static bool GroupMetadataChanged(
        ScannedRemoteEntry group,
        IReadOnlyDictionary<string, SyncedRemoteState> syncedState)
    {
        if (!syncedState.TryGetValue(group.RemotePath, out var existing))
        {
            return true;
        }

        if (!IsCompleted(existing.Status)
            && !string.Equals(existing.Status, "failed", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return existing.RemoteModifiedAt != group.RemoteModifiedAt
            || existing.FileSizeBytes != group.FileSizeBytes
            || existing.ChildCount != group.ChildCount;
    }

    private static bool IsCompleted(string status)
        => string.Equals(status, "completed", StringComparison.OrdinalIgnoreCase);

    private static bool IsRemoteDeletedCandidate(string status)
        => IsCompleted(status)
            || string.Equals(status, "failed", StringComparison.OrdinalIgnoreCase);
}
