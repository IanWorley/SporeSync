using System;
using System.Linq;
using System.Threading.Tasks;
using SftpSync.Business.Scanning;
using Xunit;

namespace SftpSync.Business.Tests.Scanning;

public sealed class FakeSftpDirectoryScannerTests
{
    [Fact]
    public async Task ConcreteExample_MatchesPlanAndRules_Exactly_VisibleFirstChildOnly_CorrectAggregates()
    {
        // Cites: plan.html:247-263 + rules.md:29-44 (diagram 2 leaves under reports/, 1 under archive/; prose "3" inconsistency at plan:302)
        // + locked decisions 1-3 (plan:216-230) + ISftpDirectoryScanner.cs:136-164
        var scanner = new FakeSftpDirectoryScanner().ConfigureConcreteIncomingExample();
        var result = await scanner.ScanFirstLevelAsync("/remote/incoming", "/data/incoming");

        Assert.Equal(3, result.VisibleEntries.Count);
        Assert.Equal(2, result.VisibleGroupCount);
        Assert.Equal(1, result.VisibleLooseFileCount);

        var byPath = result.VisibleEntries.ToDictionary(e => e.RemotePath, StringComparer.Ordinal);
        var reports = byPath["/remote/incoming/reports/"];
        Assert.True(reports.IsGroup);
        Assert.Equal(202357, reports.FileSizeBytes); // 123456 + 78901
        Assert.Equal(2, reports.ChildCount);
        Assert.Null(reports.GroupRemotePath);
        Assert.EndsWith("/", reports.RemotePath);
        Assert.NotNull(reports.RemoteModifiedAt); // MAX per rules.md:102

        var archive = byPath["/remote/incoming/archive/"];
        Assert.True(archive.IsGroup);
        Assert.Equal(4567890, archive.FileSizeBytes);
        Assert.Equal(1, archive.ChildCount);

        var customers = byPath["/remote/incoming/customers.csv"];
        Assert.False(customers.IsGroup);
        Assert.Equal(2345678, customers.FileSizeBytes);
        Assert.Equal(0, customers.ChildCount);
        Assert.Null(customers.GroupRemotePath);

        Assert.Equal(202357 + 4567890 + 2345678, result.TotalBytes);
    }

    [Fact]
    public async Task ConcreteExample_InternalLeaves_LinkCorrectly_SubtreeSumsAndCountsMatchGroup_AggregatesInvariantHolds()
    {
        // Enforces rules.md:123-127 (bytes invariant), 155-158 (linking), 167-168 (invariants), 104-111 (hybrid creation)
        var scanner = new FakeSftpDirectoryScanner().ConfigureConcreteIncomingExample();
        var result = await scanner.ScanFirstLevelAsync("/remote/incoming/", "/data/incoming/");

        var groups = result.VisibleEntries.Where(e => e.IsGroup).ToList();
        Assert.Equal(2, groups.Count);

        var reportsGroup = groups.First(g => g.RemotePath == "/remote/incoming/reports/");
        var reportsLeaves = result.InternalLeafEntries.Where(l => l.GroupRemotePath == reportsGroup.RemotePath).ToList();
        Assert.Equal(2, reportsLeaves.Count);
        Assert.All(reportsLeaves, l => Assert.False(l.IsGroup));
        Assert.All(reportsLeaves, l => Assert.Equal(0, l.ChildCount));
        Assert.All(reportsLeaves, l => Assert.EndsWith("/", l.GroupRemotePath));
        Assert.Equal(reportsGroup.FileSizeBytes, reportsLeaves.Sum(l => l.FileSizeBytes));
        Assert.Equal(reportsGroup.ChildCount, reportsLeaves.Count);
        Assert.Contains(reportsLeaves, l => l.RemotePath == "/remote/incoming/reports/2026/Q1-sales.pdf");
        Assert.Contains(reportsLeaves, l => l.RemotePath == "/remote/incoming/reports/summary.xlsx");
        // Dest paths preserve relative structure (rules.md:163-164)
        Assert.Contains(reportsLeaves, l => l.DestinationPath == "/data/incoming/reports/2026/Q1-sales.pdf");

        var archiveLeaves = result.InternalLeafEntries.Where(l => l.GroupRemotePath == "/remote/incoming/archive/").ToList();
        Assert.Single(archiveLeaves);
        Assert.Equal(4567890, archiveLeaves[0].FileSizeBytes);
    }

    [Fact]
    public async Task VisibleNeverContainsInternalLeaves_FirstChildGranularityOnly_DeeperDirsOmitted()
    {
        // Locked decision #1 (plan:217-219) + rules.md:54 + interface:139 + invariant 3 (rules:169)
        var scanner = new FakeSftpDirectoryScanner().ConfigureConcreteIncomingExample();
        var result = await scanner.ScanFirstLevelAsync("/remote/incoming", "/data/incoming");

        Assert.DoesNotContain(result.VisibleEntries, e => e.RemotePath.Contains("/2026/") || e.RemotePath.Contains("/old/"));
        Assert.All(result.VisibleEntries, e => Assert.Null(e.GroupRemotePath)); // visible never have back-link
        Assert.All(result.InternalLeafEntries, e => Assert.NotNull(e.GroupRemotePath));
    }

    [Fact]
    public async Task EmptyImmediateChildDir_CreatesGroupWithZeroes_NoLeaves_StillVisible()
    {
        // rules.md:121 (empty immediate child dirs) + 93-94 + locked #1/#4 (requeue structure preservation)
        var tree = new Dictionary<string, (bool, long, DateTimeOffset?)>(StringComparer.Ordinal)
        {
            ["/remote/incoming"] = (true, 0, null),
            ["/remote/incoming/"] = (true, 0, null),
            ["/remote/incoming/emptyDir"] = (true, 0, null),
            ["/remote/incoming/emptyDir/"] = (true, 0, null),
            ["/remote/incoming/realfile.txt"] = (false, 42, DateTimeOffset.UtcNow),
        };
        var scanner = new FakeSftpDirectoryScanner().WithRemoteEntries(tree);
        var result = await scanner.ScanFirstLevelAsync("/remote/incoming", "/out");

        var empty = result.VisibleEntries.Single(e => e.RemotePath == "/remote/incoming/emptyDir/");
        Assert.True(empty.IsGroup);
        Assert.Equal(0, empty.FileSizeBytes);
        Assert.Equal(0, empty.ChildCount);
        Assert.Null(empty.RemoteModifiedAt);
        Assert.Empty(result.InternalLeafEntries.Where(l => l.GroupRemotePath == empty.RemotePath));
    }

    [Fact]
    public async Task Normalization_WithOrWithoutTrailingSlash_IdenticalResults_ForDirTargets()
    {
        // rules.md:66-77 + interface:57-61 + plan:302 (normalization owned by scanner)
        var scanner = new FakeSftpDirectoryScanner().ConfigureConcreteIncomingExample();
        var r1 = await scanner.ScanFirstLevelAsync("/remote/incoming", "/data/incoming");
        var r2 = await scanner.ScanFirstLevelAsync("/remote/incoming/", "/data/incoming/");

        Assert.Equal(r1.VisibleEntries.Count, r2.VisibleEntries.Count);
        Assert.Equal(r1.TotalBytes, r2.TotalBytes);
        Assert.Equal(r1.VisibleGroupCount, r2.VisibleGroupCount);
        Assert.Equal(
            r1.VisibleEntries.Select(e => e.RemotePath).OrderBy(x => x),
            r2.VisibleEntries.Select(e => e.RemotePath).OrderBy(x => x));
    }

    [Fact]
    public async Task FileTarget_DirectOrDeepSourcePath_SingleLooseFile_VisibleOnly_NoGroupsOrInternalLeaves()
    {
        // rules.md:83-84 + 142 + interface:64 + edge case
        var scanner = new FakeSftpDirectoryScanner().ConfigureConcreteIncomingExample();
        var result = await scanner.ScanFirstLevelAsync("/remote/incoming/customers.csv", "/data/out.csv");

        Assert.Single(result.VisibleEntries);
        var f = result.VisibleEntries[0];
        Assert.False(f.IsGroup);
        Assert.Equal("/remote/incoming/customers.csv", f.RemotePath); // verbatim
        Assert.Equal("/data/out.csv", f.DestinationPath); // verbatim for file target
        Assert.Equal(0, result.VisibleGroupCount);
        Assert.Equal(1, result.VisibleLooseFileCount);
        Assert.Empty(result.InternalLeafEntries);
        Assert.Equal(2345678, result.TotalBytes);

        // Deep file target still single loose (no auto-grouping)
        var deep = await scanner.ScanFirstLevelAsync("/remote/incoming/reports/2026/Q1-sales.pdf", "/data/deep.pdf");
        Assert.Single(deep.VisibleEntries);
        Assert.False(deep.VisibleEntries[0].IsGroup);
        Assert.Empty(deep.InternalLeafEntries);
    }

    [Fact]
    public async Task DestinationPaths_PreserveRelativeStructure_ForGroupsAndAllLeaves()
    {
        var scanner = new FakeSftpDirectoryScanner().ConfigureConcreteIncomingExample();
        var result = await scanner.ScanFirstLevelAsync("/remote/incoming", "/data/incoming");

        var reports = result.VisibleEntries.Single(e => e.RemotePath.EndsWith("reports/"));
        Assert.Equal("/data/incoming/reports/", reports.DestinationPath);
        var q1 = result.InternalLeafEntries.Single(l => l.RemotePath.EndsWith("Q1-sales.pdf"));
        Assert.Equal("/data/incoming/reports/2026/Q1-sales.pdf", q1.DestinationPath);
    }
}
