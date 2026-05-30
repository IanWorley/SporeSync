using SftpSync.Business.Scanning;
using SftpSync.Domain.Model;

namespace SftpSync.Business.Worker;

public interface IChangeDetector
{
    ChangeDetectionResult DetectChanges(
        FirstLevelScanResult scanResult,
        IReadOnlyDictionary<string, SyncedRemoteState> syncedState);
}

public sealed record ChangeDetectionResult(
    IReadOnlyList<ScannedRemoteEntry> EntriesToEnqueue,
    int EnqueuedVisibleCount,
    long EnqueuedTotalBytes);

public sealed class ChangeDetector : IChangeDetector
{
    public ChangeDetectionResult DetectChanges(
        FirstLevelScanResult scanResult,
        IReadOnlyDictionary<string, SyncedRemoteState> syncedState)
    {
        var toEnqueue = new List<ScannedRemoteEntry>();
        var visiblePaths = new HashSet<string>(
            scanResult.VisibleEntries.Select(entry => entry.RemotePath),
            StringComparer.Ordinal);

        foreach (var visible in scanResult.VisibleEntries)
        {
            if (ShouldEnqueue(visible, syncedState))
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

        return new ChangeDetectionResult(toEnqueue, enqueuedVisibleCount, enqueuedTotalBytes);
    }

    private static bool ShouldEnqueue(
        ScannedRemoteEntry entry,
        IReadOnlyDictionary<string, SyncedRemoteState> syncedState)
    {
        if (!syncedState.TryGetValue(entry.RemotePath, out var existing))
        {
            return true;
        }

        if (string.Equals(existing.Status, "failed", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (string.Equals(existing.Status, "completed", StringComparison.OrdinalIgnoreCase)
            && existing.RemoteModifiedAt == entry.RemoteModifiedAt
            && existing.FileSizeBytes == entry.FileSizeBytes)
        {
            return false;
        }

        return true;
    }
}
