using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using SftpSync.Business.Scanning;

namespace SftpSync.Business.Tests.Scanning;

/// <summary>
/// Fully deterministic, injectable, in-memory fake for ISftpDirectoryScanner (Phase 4 / M3).
/// Embodies grouping-rules.md (single source of truth) + locked decisions (plan.html:216-239)
/// + interface contract (ISftpDirectoryScanner.cs:90-165). No network, no FS, no DB, no SSH.NET.
/// </summary>
public sealed class FakeSftpDirectoryScanner : ISftpDirectoryScanner
{
    private Dictionary<string, (bool IsDir, long Size, DateTimeOffset? Mtime)> _remote = new(StringComparer.Ordinal);

    /// <summary>
    /// Replaces the simulated remote tree (canonical keys: dirs end with '/', files do not).
    /// Call before ScanFirstLevelAsync. Deterministic; supports ConfigureConcreteIncomingExample.
    /// </summary>
    public FakeSftpDirectoryScanner WithRemoteEntries(IDictionary<string, (bool IsDir, long Size, DateTimeOffset? Mtime)> entries)
    {
        _remote = new Dictionary<string, (bool, long, DateTimeOffset?)>(entries, StringComparer.Ordinal);
        return this;
    }

    /// <summary>
    /// Configures exactly the concrete example tree (plan.html:247-255 + rules.md:29-38).
    /// Uses diagram leaf counts (2 under reports/, 1 under archive/) — prose "3" at plan:258/rules:42 is the documented inconsistency (plan:302).
    /// Fixed sizes/mtimes for determinism. Source often lacks trailing / (see DevelopmentSimulationService.cs:69 + ISftpDirectoryScanner.cs:58).
    /// </summary>
    public FakeSftpDirectoryScanner ConfigureConcreteIncomingExample()
    {
        var now = DateTimeOffset.Parse("2026-05-28T12:00:00Z");
        var tree = new Dictionary<string, (bool, long, DateTimeOffset?)>(StringComparer.Ordinal)
        {
            // Both with and without trailing / for robustness with job SourcePath (common case per DevelopmentSimulationService.cs:69)
            ["/remote/incoming"] = (true, 0, now),
            ["/remote/incoming/"] = (true, 0, now),
            ["/remote/incoming/reports"] = (true, 0, now),
            ["/remote/incoming/reports/"] = (true, 0, now),
            ["/remote/incoming/reports/2026"] = (true, 0, now),
            ["/remote/incoming/reports/2026/"] = (true, 0, now),
            ["/remote/incoming/archive"] = (true, 0, now),
            ["/remote/incoming/archive/"] = (true, 0, now),
            ["/remote/incoming/archive/old"] = (true, 0, now),
            ["/remote/incoming/archive/old/"] = (true, 0, now),

            // Leaves (files, no trailing /)
            ["/remote/incoming/customers.csv"] = (false, 2345678, now.AddHours(-2)),
            ["/remote/incoming/reports/summary.xlsx"] = (false, 78901, now.AddDays(-1).AddHours(-3)),
            ["/remote/incoming/reports/2026/Q1-sales.pdf"] = (false, 123456, now.AddDays(-8).AddHours(2.5)),
            ["/remote/incoming/archive/old/backup.zip"] = (false, 4567890, DateTimeOffset.Parse("2025-12-01T00:00:00Z")),
        };
        return WithRemoteEntries(tree);
    }

    public async Task<FirstLevelScanResult> ScanFirstLevelAsync(
        string sourcePath,
        string destinationPath,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var (isFileTarget, canonicalSource, normalizedSource) = ResolveTarget(sourcePath);

        if (isFileTarget)
        {
            // File target (rules.md:83-84, interface:64): exactly one visible loose file; use sourcePath verbatim for remote_path
            var info = _remote.TryGetValue(canonicalSource, out var i) ? i : (IsDir: false, Size: 0L, Mtime: (DateTimeOffset?)null);
            var dest = destinationPath; // verbatim for direct file target (underspec noted in interface tweaks below)
            var entry = new ScannedRemoteEntry(
                sourcePath, // verbatim per rules
                dest,
                IsGroup: false,
                FileSizeBytes: info.Size,
                ChildCount: 0,
                GroupRemotePath: null,
                RemoteModifiedAt: info.Mtime);
            return new FirstLevelScanResult(
                VisibleEntries: new[] { entry },
                InternalLeafEntries: Array.Empty<ScannedRemoteEntry>(),
                TotalBytes: entry.FileSizeBytes,
                VisibleGroupCount: 0,
                VisibleLooseFileCount: 1);
        }

        // Directory target (rules.md:85-121): normalize, first-child only, hybrid groups+leaves
        var normSrc = normalizedSource;
        var normDest = NormalizeForAppend(destinationPath);

        var children = GetImmediateChildren(normSrc).OrderBy(c => c.Name, StringComparer.Ordinal).ToList(); // deterministic order
        var visible = new List<ScannedRemoteEntry>();
        var leaves = new List<ScannedRemoteEntry>();
        long totalBytes = 0;
        int groupCount = 0, looseCount = 0;

        foreach (var child in children)
        {
            var childRemote = normSrc + child.Name + (child.IsDir ? "/" : "");
            var childDest = normDest + child.Name + (child.IsDir ? "/" : "");

            if (child.IsDir)
            {
                var subtreeLeaves = CollectLeavesUnder(childRemote, normSrc, normDest);
                var subtreeBytes = subtreeLeaves.Sum(l => l.FileSizeBytes);
                var subtreeCount = subtreeLeaves.Count;
                var maxMtime = subtreeLeaves.Count > 0 ? subtreeLeaves.Max(l => l.RemoteModifiedAt) : (DateTimeOffset?)null;

                var group = new ScannedRemoteEntry(
                    childRemote,
                    childDest,
                    IsGroup: true,
                    FileSizeBytes: subtreeBytes,
                    ChildCount: subtreeCount,
                    GroupRemotePath: null,
                    RemoteModifiedAt: maxMtime); // MAX per rules.md:102 (interface XML comment at :128 is the mismatch)
                visible.Add(group);
                leaves.AddRange(subtreeLeaves);
                totalBytes += subtreeBytes;
                groupCount++;
            }
            else
            {
                var loose = new ScannedRemoteEntry(
                    childRemote,
                    childDest,
                    IsGroup: false,
                    FileSizeBytes: child.Size,
                    ChildCount: 0,
                    GroupRemotePath: null,
                    RemoteModifiedAt: child.Mtime);
                visible.Add(loose);
                totalBytes += child.Size;
                looseCount++;
            }
        }

        return new FirstLevelScanResult(
            VisibleEntries: visible,
            InternalLeafEntries: leaves,
            TotalBytes: totalBytes,
            VisibleGroupCount: groupCount,
            VisibleLooseFileCount: looseCount);
    }

    private (bool IsFile, string Canonical, string Normalized) ResolveTarget(string sourcePath)
    {
        var s = sourcePath?.Trim() ?? string.Empty;
        // Try exact
        if (_remote.TryGetValue(s, out var exact))
            return (!exact.IsDir, s, exact.IsDir ? NormalizeDir(s) : s);

        // Try both with and without trailing slash (robustness for job SourcePath convention)
        var candidates = new[] { s, s.TrimEnd('/'), s.TrimEnd('/') + "/" };
        foreach (var c in candidates.Distinct())
        {
            if (_remote.TryGetValue(c, out var info) && info.IsDir)
            {
                var norm = NormalizeDir(c);
                return (false, norm, norm);
            }
        }

        // Non-existent: treat as dir target (rules.md:65)
        var norm2 = NormalizeDir(s);
        return (false, norm2, norm2);
    }

    private static string NormalizeDir(string p)
    {
        var s = (p ?? string.Empty).Trim();
        return s.EndsWith('/') ? s : s + "/";
    }

    private static string NormalizeForAppend(string p)
    {
        var s = (p ?? string.Empty).Trim();
        return s.EndsWith('/') ? s : s + "/";
    }

    private List<(string Name, bool IsDir, long Size, DateTimeOffset? Mtime)> GetImmediateChildren(string normSource)
    {
        var result = new List<(string, bool, long, DateTimeOffset?)>();
        foreach (var kv in _remote)
        {
            var path = kv.Key;
            if (!path.StartsWith(normSource, StringComparison.Ordinal) || path == normSource) continue;
            var rest = path.Substring(normSource.Length);
            if (rest.Contains('/')) continue; // deeper than immediate
            var name = rest.TrimEnd('/');
            result.Add((name, kv.Value.IsDir, kv.Value.Size, kv.Value.Mtime));
        }
        return result;
    }

    private List<ScannedRemoteEntry> CollectLeavesUnder(string groupRemote, string normSource, string normDest)
    {
        // All descendant files (not dirs) under this first-child group (rules.md:91,104-111)
        var collected = new List<ScannedRemoteEntry>();
        var groupPrefix = groupRemote; // ends with /
        foreach (var kv in _remote)
        {
            var path = kv.Key;
            if (kv.Value.IsDir || !path.StartsWith(groupPrefix, StringComparison.Ordinal)) continue;
            var relative = path.Substring(normSource.Length); // includes the group segment + deeper
            var dest = normDest + relative;
            collected.Add(new ScannedRemoteEntry(
                path,
                dest,
                IsGroup: false,
                FileSizeBytes: kv.Value.Size,
                ChildCount: 0,
                GroupRemotePath: groupRemote, // exact match incl. trailing / (rules.md:108,155-156 invariant)
                RemoteModifiedAt: kv.Value.Mtime));
        }
        return collected;
    }
}
