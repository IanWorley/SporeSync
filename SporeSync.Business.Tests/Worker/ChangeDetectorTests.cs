using SporeSync.Business.Scanning;
using SporeSync.Business.Worker;
using SporeSync.Domain.Model;

namespace SporeSync.Business.Tests.Worker;

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
    public void DetectChanges_UnchangedGroupWithMissingLocalLeaf_ReEnqueuesOnlyMissingLeaf()
    {
        var modifiedAt = DateTimeOffset.Parse("2026-05-28T12:00:00Z");
        var detector = new ChangeDetector();
        var group = new ScannedRemoteEntry("/remote/reports/", "/data/reports/", true, 300, 2, null, modifiedAt);
        var presentLeaf = new ScannedRemoteEntry("/remote/reports/a.txt", CreateExistingTempFile(), false, 100, 0, "/remote/reports/", modifiedAt);
        var missingLeaf = new ScannedRemoteEntry("/remote/reports/b.txt", CreateMissingTempPath(), false, 200, 0, "/remote/reports/", modifiedAt);
        var scan = new FirstLevelScanResult(
            new[] { group },
            new[] { presentLeaf, missingLeaf },
            300,
            1,
            0);
        var synced = CreateCompletedGroupState(modifiedAt, presentLeaf, missingLeaf);

        var result = detector.DetectChanges(scan, synced);

        Assert.Equal(
            new[] { missingLeaf.RemotePath, group.RemotePath },
            result.EntriesToEnqueue.Select(entry => entry.RemotePath));
        var carried = Assert.Single(result.EntriesToCarryForward);
        Assert.Equal(presentLeaf.RemotePath, carried.RemotePath);
        Assert.Equal(1, result.EnqueuedVisibleCount);
    }

    [Fact]
    public void DetectChanges_GroupWithOneChangedLeaf_EnqueuesOnlyChangedLeafAndCarriesRestForward()
    {
        var modifiedAt = DateTimeOffset.Parse("2026-05-28T12:00:00Z");
        var detector = new ChangeDetector();
        var unchangedLeaf = new ScannedRemoteEntry("/remote/reports/a.txt", CreateExistingTempFile(), false, 100, 0, "/remote/reports/", modifiedAt);
        var changedLeaf = new ScannedRemoteEntry("/remote/reports/b.txt", CreateExistingTempFile(), false, 250, 0, "/remote/reports/", modifiedAt.AddHours(1));
        var group = new ScannedRemoteEntry("/remote/reports/", "/data/reports/", true, 350, 2, null, modifiedAt.AddHours(1));
        var scan = new FirstLevelScanResult(
            new[] { group },
            new[] { unchangedLeaf, changedLeaf },
            350,
            1,
            0);
        var synced = new Dictionary<string, SyncedRemoteState>(StringComparer.Ordinal)
        {
            ["/remote/reports/"] = CompletedState("/remote/reports/", modifiedAt, 300, childCount: 2),
            [unchangedLeaf.RemotePath] = CompletedState(unchangedLeaf.RemotePath, modifiedAt, 100),
            [changedLeaf.RemotePath] = CompletedState(changedLeaf.RemotePath, modifiedAt, 200)
        };

        var result = detector.DetectChanges(scan, synced);

        Assert.Equal(
            new[] { changedLeaf.RemotePath, group.RemotePath },
            result.EntriesToEnqueue.Select(entry => entry.RemotePath));
        var carried = Assert.Single(result.EntriesToCarryForward);
        Assert.Equal(unchangedLeaf.RemotePath, carried.RemotePath);
        Assert.Equal(1, result.EnqueuedVisibleCount);
        Assert.Equal(350, result.EnqueuedTotalBytes);
    }

    [Fact]
    public void DetectChanges_CompensatingLeafChanges_AreDetectedDespiteIdenticalGroupFingerprint()
    {
        // One leaf grows by the same amount another shrinks, and neither mtime exceeds the
        // previous group maximum: the lossy group-level fingerprint (byte sum + max mtime)
        // is unchanged, but leaf-level diffing must still catch both changed files.
        var modifiedAt = DateTimeOffset.Parse("2026-05-28T12:00:00Z");
        var detector = new ChangeDetector();
        var grownLeaf = new ScannedRemoteEntry("/remote/reports/a.txt", CreateExistingTempFile(), false, 200, 0, "/remote/reports/", modifiedAt);
        var shrunkLeaf = new ScannedRemoteEntry("/remote/reports/b.txt", CreateExistingTempFile(), false, 100, 0, "/remote/reports/", modifiedAt);
        var group = new ScannedRemoteEntry("/remote/reports/", "/data/reports/", true, 300, 2, null, modifiedAt);
        var scan = new FirstLevelScanResult(
            new[] { group },
            new[] { grownLeaf, shrunkLeaf },
            300,
            1,
            0);
        var synced = new Dictionary<string, SyncedRemoteState>(StringComparer.Ordinal)
        {
            ["/remote/reports/"] = CompletedState("/remote/reports/", modifiedAt, 300, childCount: 2),
            [grownLeaf.RemotePath] = CompletedState(grownLeaf.RemotePath, modifiedAt, 100),
            [shrunkLeaf.RemotePath] = CompletedState(shrunkLeaf.RemotePath, modifiedAt, 200)
        };

        var result = detector.DetectChanges(scan, synced);

        Assert.Equal(
            new[] { grownLeaf.RemotePath, shrunkLeaf.RemotePath, group.RemotePath },
            result.EntriesToEnqueue.Select(entry => entry.RemotePath));
        Assert.Empty(result.EntriesToCarryForward);
    }

    [Fact]
    public void DetectChanges_GroupWithRemoteDeletedLeaf_RefreshesGroupAndCarriesRemainingLeaves()
    {
        var modifiedAt = DateTimeOffset.Parse("2026-05-28T12:00:00Z");
        var detector = new ChangeDetector();
        var remainingLeaf = new ScannedRemoteEntry("/remote/reports/a.txt", CreateExistingTempFile(), false, 100, 0, "/remote/reports/", modifiedAt);
        var group = new ScannedRemoteEntry("/remote/reports/", "/data/reports/", true, 100, 1, null, modifiedAt);
        var scan = new FirstLevelScanResult(
            new[] { group },
            new[] { remainingLeaf },
            100,
            1,
            0);
        var synced = new Dictionary<string, SyncedRemoteState>(StringComparer.Ordinal)
        {
            ["/remote/reports/"] = CompletedState("/remote/reports/", modifiedAt, 300, childCount: 2),
            [remainingLeaf.RemotePath] = CompletedState(remainingLeaf.RemotePath, modifiedAt, 100),
            ["/remote/reports/deleted.txt"] = CompletedState("/remote/reports/deleted.txt", modifiedAt, 200)
        };

        var result = detector.DetectChanges(scan, synced);

        var enqueued = Assert.Single(result.EntriesToEnqueue);
        Assert.Equal(group.RemotePath, enqueued.RemotePath);
        var carried = Assert.Single(result.EntriesToCarryForward);
        Assert.Equal(remainingLeaf.RemotePath, carried.RemotePath);
        Assert.Equal(new[] { "/remote/reports/deleted.txt" }, result.RemoteDeletedPaths);
    }

    [Fact]
    public void DetectChanges_FullyUnchangedGroup_IsSkipped()
    {
        var modifiedAt = DateTimeOffset.Parse("2026-05-28T12:00:00Z");
        var detector = new ChangeDetector();
        var leaf = new ScannedRemoteEntry("/remote/reports/a.txt", CreateExistingTempFile(), false, 100, 0, "/remote/reports/", modifiedAt);
        var group = new ScannedRemoteEntry("/remote/reports/", "/data/reports/", true, 100, 1, null, modifiedAt);
        var scan = new FirstLevelScanResult(
            new[] { group },
            new[] { leaf },
            100,
            1,
            0);
        var synced = new Dictionary<string, SyncedRemoteState>(StringComparer.Ordinal)
        {
            ["/remote/reports/"] = CompletedState("/remote/reports/", modifiedAt, 100, childCount: 1),
            [leaf.RemotePath] = CompletedState(leaf.RemotePath, modifiedAt, 100)
        };

        var result = detector.DetectChanges(scan, synced);

        Assert.Empty(result.EntriesToEnqueue);
        Assert.Empty(result.EntriesToCarryForward);
        Assert.Equal(0, result.EnqueuedVisibleCount);
    }

    [Fact]
    public void DetectChanges_UnchangedFailedEntry_StaysDeadLettered()
    {
        // A dead-lettered ('failed') item must not be auto-requeued while the remote file is
        // unchanged, otherwise every scan would restart the retry loop forever.
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

        Assert.Empty(result.EntriesToEnqueue);
    }

    [Fact]
    public void DetectChanges_ChangedFailedEntry_IsReEnqueued()
    {
        var detector = new ChangeDetector();
        var scan = CreateScan(
            new ScannedRemoteEntry("/remote/file.txt", "/data/file.txt", false, 250, 0, null, DateTimeOffset.UtcNow));
        var synced = new Dictionary<string, SyncedRemoteState>(StringComparer.Ordinal)
        {
            ["/remote/file.txt"] = new SyncedRemoteState
            {
                RemotePath = "/remote/file.txt",
                RemoteModifiedAt = DateTimeOffset.Parse("2026-01-01T00:00:00Z"),
                FileSizeBytes = 100,
                Status = "failed"
            }
        };

        var result = detector.DetectChanges(scan, synced);

        Assert.Single(result.EntriesToEnqueue);
    }

    [Fact]
    public void DetectChanges_UnchangedQueuedEntry_IsNotReEnqueued()
    {
        // Re-upserting an in-flight/queued item would reset its progress and retry budget.
        var modifiedAt = DateTimeOffset.Parse("2026-05-28T12:00:00Z");
        var detector = new ChangeDetector();
        var scan = CreateScan(
            new ScannedRemoteEntry("/remote/file.txt", "/data/file.txt", false, 100, 0, null, modifiedAt));
        var synced = new Dictionary<string, SyncedRemoteState>(StringComparer.Ordinal)
        {
            ["/remote/file.txt"] = new SyncedRemoteState
            {
                RemotePath = "/remote/file.txt",
                RemoteModifiedAt = modifiedAt,
                FileSizeBytes = 100,
                Status = "queued"
            }
        };

        var result = detector.DetectChanges(scan, synced);

        Assert.Empty(result.EntriesToEnqueue);
    }

    [Fact]
    public void DetectChanges_UnchangedSkippedEntry_IsReEnqueued()
    {
        // A previously remote-deleted (skipped) path that reappears should download again.
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
                Status = "skipped"
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
        // The group row must be enqueued after its leaves so the worker can never claim
        // the group before its leaf rows exist.
        Assert.Equal(group.RemotePath, result.EntriesToEnqueue[^1].RemotePath);
        Assert.Equal(1, result.EnqueuedVisibleCount);
        Assert.Equal(300, result.EnqueuedTotalBytes);
    }

    private static Dictionary<string, SyncedRemoteState> CreateCompletedGroupState(
        DateTimeOffset modifiedAt,
        params ScannedRemoteEntry[] leaves)
    {
        var synced = new Dictionary<string, SyncedRemoteState>(StringComparer.Ordinal)
        {
            ["/remote/reports/"] = CompletedState(
                "/remote/reports/",
                modifiedAt,
                leaves.Sum(leaf => leaf.FileSizeBytes),
                childCount: leaves.Length)
        };

        foreach (var leaf in leaves)
        {
            synced[leaf.RemotePath] = CompletedState(leaf.RemotePath, modifiedAt, leaf.FileSizeBytes);
        }

        return synced;
    }

    private static SyncedRemoteState CompletedState(
        string remotePath,
        DateTimeOffset? modifiedAt,
        long fileSizeBytes,
        int childCount = 0)
    {
        return new SyncedRemoteState
        {
            RemotePath = remotePath,
            RemoteModifiedAt = modifiedAt,
            FileSizeBytes = fileSizeBytes,
            Status = "completed",
            ChildCount = childCount
        };
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
        var path = Path.Combine(Path.GetTempPath(), $"sporesync-test-{Guid.NewGuid():N}.tmp");
        File.WriteAllText(path, "downloaded");
        return path;
    }

    private static string CreateMissingTempPath()
        => Path.Combine(Path.GetTempPath(), $"sporesync-test-{Guid.NewGuid():N}.tmp");
}
