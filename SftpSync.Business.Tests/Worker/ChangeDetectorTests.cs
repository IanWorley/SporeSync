using SftpSync.Business.Scanning;
using SftpSync.Business.Worker;
using SftpSync.Domain.Model;

namespace SftpSync.Business.Tests.Worker;

public sealed class ChangeDetectorTests
{
    [Fact]
    public void DetectChanges_NewEntry_IsEnqueued()
    {
        var detector = new ChangeDetector();
        var scan = CreateScan(
            new ScannedRemoteEntry("/remote/file.txt", "/data/file.txt", false, 100, 0, null, null));

        var result = detector.DetectChanges(scan, new Dictionary<string, SyncedRemoteState>());

        Assert.Single(result.EntriesToEnqueue);
        Assert.Equal(1, result.EnqueuedVisibleCount);
        Assert.Equal(100, result.EnqueuedTotalBytes);
    }

    [Fact]
    public void DetectChanges_UnchangedCompletedEntry_IsSkipped()
    {
        var modifiedAt = DateTimeOffset.Parse("2026-05-28T12:00:00Z");
        var destinationPath = CreateExistingTempFile();
        var detector = new ChangeDetector();
        var scan = CreateScan(
            new ScannedRemoteEntry("/remote/file.txt", destinationPath, false, 100, 0, null, modifiedAt));
        var synced = new Dictionary<string, SyncedRemoteState>(StringComparer.Ordinal)
        {
            ["/remote/file.txt"] = new SyncedRemoteState
            {
                RemotePath = "/remote/file.txt",
                RemoteModifiedAt = modifiedAt,
                FileSizeBytes = 100,
                Status = "completed"
            }
        };

        var result = detector.DetectChanges(scan, synced);

        Assert.Empty(result.EntriesToEnqueue);
        Assert.Empty(result.RemoteDeletedPaths);
        Assert.Equal(0, result.EnqueuedVisibleCount);
    }

    [Fact]
    public void DetectChanges_UnchangedCompletedEntryWithMissingLocalFile_IsReEnqueued()
    {
        var modifiedAt = DateTimeOffset.Parse("2026-05-28T12:00:00Z");
        var detector = new ChangeDetector();
        var scan = CreateScan(
            new ScannedRemoteEntry("/remote/file.txt", CreateMissingTempPath(), false, 100, 0, null, modifiedAt));
        var synced = new Dictionary<string, SyncedRemoteState>(StringComparer.Ordinal)
        {
            ["/remote/file.txt"] = new SyncedRemoteState
            {
                RemotePath = "/remote/file.txt",
                RemoteModifiedAt = modifiedAt,
                FileSizeBytes = 100,
                Status = "completed"
            }
        };

        var result = detector.DetectChanges(scan, synced);

        Assert.Single(result.EntriesToEnqueue);
    }

    [Fact]
    public void DetectChanges_ChangedCompletedEntry_IsReEnqueued()
    {
        var detector = new ChangeDetector();
        var scan = CreateScan(
            new ScannedRemoteEntry("/remote/file.txt", "/data/file.txt", false, 200, 0, null, DateTimeOffset.UtcNow));
        var synced = new Dictionary<string, SyncedRemoteState>(StringComparer.Ordinal)
        {
            ["/remote/file.txt"] = new SyncedRemoteState
            {
                RemotePath = "/remote/file.txt",
                RemoteModifiedAt = DateTimeOffset.Parse("2026-01-01T00:00:00Z"),
                FileSizeBytes = 100,
                Status = "completed"
            }
        };

        var result = detector.DetectChanges(scan, synced);

        Assert.Single(result.EntriesToEnqueue);
    }

    [Fact]
    public void DetectChanges_CompletedRemotePathMissingFromScan_IsRemoteDeleted()
    {
        var detector = new ChangeDetector();
        var scan = CreateScan(
            new ScannedRemoteEntry("/remote/remaining.txt", CreateMissingTempPath(), false, 100, 0, null, null));
        var synced = new Dictionary<string, SyncedRemoteState>(StringComparer.Ordinal)
        {
            ["/remote/deleted.txt"] = new SyncedRemoteState
            {
                RemotePath = "/remote/deleted.txt",
                RemoteModifiedAt = null,
                FileSizeBytes = 100,
                Status = "completed"
            },
            ["/remote/remaining.txt"] = new SyncedRemoteState
            {
                RemotePath = "/remote/remaining.txt",
                RemoteModifiedAt = null,
                FileSizeBytes = 100,
                Status = "completed"
            }
        };

        var result = detector.DetectChanges(scan, synced);

        Assert.Equal(new[] { "/remote/deleted.txt" }, result.RemoteDeletedPaths);
    }

    [Fact]
    public void DetectChanges_UnchangedGroupWithMissingLocalLeaf_IsReEnqueuedWithLeaves()
    {
        var modifiedAt = DateTimeOffset.Parse("2026-05-28T12:00:00Z");
        var detector = new ChangeDetector();
        var group = new ScannedRemoteEntry("/remote/reports/", "/data/reports/", true, 300, 2, null, modifiedAt);
        var leaf1 = new ScannedRemoteEntry("/remote/reports/a.txt", CreateExistingTempFile(), false, 100, 0, "/remote/reports/", modifiedAt);
        var leaf2 = new ScannedRemoteEntry("/remote/reports/b.txt", CreateMissingTempPath(), false, 200, 0, "/remote/reports/", modifiedAt);
        var scan = new FirstLevelScanResult(
            new[] { group },
            new[] { leaf1, leaf2 },
            300,
            1,
            0);
        var synced = new Dictionary<string, SyncedRemoteState>(StringComparer.Ordinal)
        {
            ["/remote/reports/"] = new SyncedRemoteState
            {
                RemotePath = "/remote/reports/",
                RemoteModifiedAt = modifiedAt,
                FileSizeBytes = 300,
                Status = "completed"
            }
        };

        var result = detector.DetectChanges(scan, synced);

        Assert.Equal(3, result.EntriesToEnqueue.Count);
        Assert.Equal(1, result.EnqueuedVisibleCount);
    }

    [Fact]
    public void DetectChanges_FailedEntry_IsReEnqueued()
    {
        var detector = new ChangeDetector();
        var scan = CreateScan(
            new ScannedRemoteEntry("/remote/file.txt", "/data/file.txt", false, 100, 0, null, null));
        var synced = new Dictionary<string, SyncedRemoteState>(StringComparer.Ordinal)
        {
            ["/remote/file.txt"] = new SyncedRemoteState
            {
                RemotePath = "/remote/file.txt",
                RemoteModifiedAt = null,
                FileSizeBytes = 100,
                Status = "failed"
            }
        };

        var result = detector.DetectChanges(scan, synced);

        Assert.Single(result.EntriesToEnqueue);
    }

    [Fact]
    public void DetectChanges_GroupChange_EnqueuesGroupAndLeaves()
    {
        var detector = new ChangeDetector();
        var group = new ScannedRemoteEntry("/remote/reports/", "/data/reports/", true, 300, 2, null, null);
        var leaf1 = new ScannedRemoteEntry("/remote/reports/a.txt", "/data/reports/a.txt", false, 100, 0, "/remote/reports/", null);
        var leaf2 = new ScannedRemoteEntry("/remote/reports/b.txt", "/data/reports/b.txt", false, 200, 0, "/remote/reports/", null);
        var scan = new FirstLevelScanResult(
            new[] { group },
            new[] { leaf1, leaf2 },
            300,
            1,
            0);

        var result = detector.DetectChanges(scan, new Dictionary<string, SyncedRemoteState>());

        Assert.Equal(3, result.EntriesToEnqueue.Count);
        Assert.Equal(1, result.EnqueuedVisibleCount);
        Assert.Equal(300, result.EnqueuedTotalBytes);
    }

    private static FirstLevelScanResult CreateScan(params ScannedRemoteEntry[] visible)
    {
        return new FirstLevelScanResult(
            visible,
            Array.Empty<ScannedRemoteEntry>(),
            visible.Sum(entry => entry.FileSizeBytes),
            visible.Count(entry => entry.IsGroup),
            visible.Count(entry => !entry.IsGroup));
    }

    private static string CreateExistingTempFile()
    {
        var path = Path.Combine(Path.GetTempPath(), $"sftpsync-test-{Guid.NewGuid():N}.tmp");
        File.WriteAllText(path, "downloaded");
        return path;
    }

    private static string CreateMissingTempPath()
        => Path.Combine(Path.GetTempPath(), $"sftpsync-test-{Guid.NewGuid():N}.tmp");
}
