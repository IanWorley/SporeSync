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
    IReadOnlyList<string> RemoteDeletedPaths,
    int EnqueuedVisibleCount,
    long EnqueuedTotalBytes);

public sealed class ChangeDetector : IChangeDetector
{
    public ChangeDetectionResult DetectChanges(
        FirstLevelScanResult scanResult,
        IReadOnlyDictionary<string, SyncedRemoteState> syncedState)
    {
        var toEnqueue = new List<ScannedRemoteEntry>();
        var scannedRemotePaths = scanResult.VisibleEntries
            .Concat(scanResult.InternalLeafEntries)
            .Select(entry => entry.RemotePath)
            .ToHashSet(StringComparer.Ordinal);
        var remoteDeletedPaths = syncedState.Values
            .Where(state => IsCompleted(state.Status) && !scannedRemotePaths.Contains(state.RemotePath))
            .Select(state => state.RemotePath)
            .ToArray();
        var visiblePaths = new HashSet<string>(
            scanResult.VisibleEntries.Select(entry => entry.RemotePath),
            StringComparer.Ordinal);
        var leavesByGroup = scanResult.InternalLeafEntries
            .Where(leaf => leaf.GroupRemotePath is not null)
            .GroupBy(leaf => leaf.GroupRemotePath, StringComparer.Ordinal)
            .ToDictionary(group => group.Key!, group => group.ToArray(), StringComparer.Ordinal);

        foreach (var visible in scanResult.VisibleEntries)
        {
            if (ShouldEnqueue(visible, syncedState, leavesByGroup))
            {
                toEnqueue.Add(visible);
                if (visible.IsGroup)
                {
                    var groupLeaves = scanResult.InternalLeafEntries
                        .Where(leaf => string.Equals(leaf.GroupRemotePath, visible.RemotePath, StringComparison.Ordinal));
                    toEnqueue.AddRange(groupLeaves);
                }
            }
        }

        var enqueuedVisibleCount = toEnqueue.Count(entry =>
            visiblePaths.Contains(entry.RemotePath));
        var enqueuedTotalBytes = scanResult.VisibleEntries
            .Where(entry => toEnqueue.Any(enqueued => enqueued.RemotePath == entry.RemotePath))
            .Sum(entry => entry.FileSizeBytes);

        return new ChangeDetectionResult(toEnqueue, remoteDeletedPaths, enqueuedVisibleCount, enqueuedTotalBytes);
    }

    private static bool ShouldEnqueue(
        ScannedRemoteEntry entry,
        IReadOnlyDictionary<string, SyncedRemoteState> syncedState,
        IReadOnlyDictionary<string, ScannedRemoteEntry[]> leavesByGroup)
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
            return IsLocalDestinationMissing(entry, leavesByGroup);
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

    private static bool IsLocalDestinationMissing(
        ScannedRemoteEntry entry,
        IReadOnlyDictionary<string, ScannedRemoteEntry[]> leavesByGroup)
    {
        if (!entry.IsGroup)
        {
            return !File.Exists(entry.DestinationPath);
        }

        return leavesByGroup.TryGetValue(entry.RemotePath, out var leaves)
            && leaves.Any(leaf => !File.Exists(leaf.DestinationPath));
    }

    private static bool IsCompleted(string status)
        => string.Equals(status, "completed", StringComparison.OrdinalIgnoreCase);
}
