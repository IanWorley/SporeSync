using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace SftpSync.Business.Scanning;

/// <summary>
/// Contract for SFTP directory scanning and first-child enqueue preparation (Phase 0 / M3).
/// The future real worker (and any test code) calls this to obtain the exact set of
/// visible first-child groups + loose files plus all hybrid internal leaf rows needed
/// for a job's sourcePath, following the locked hybrid model.
/// </summary>
/// <remarks>
/// <para>
/// This interface is purely additive and 100% backward-compatible (see confirmation below).
/// Nothing in the current flat-file simulation, DevelopmentSimulationService, controllers,
/// DTOs, SignalR, repositories, SQL functions, or public APIs calls or depends on it.
/// </para>
/// <para>
/// <strong>References (do not deviate):</strong>
/// - 5 Locked Design Decisions + full plan in docs/folder-grouping-implementation-plan.html (Phase 9 note: this contract + grouping-rules.md are the authoritative spec for any future real SFTP worker implementation)
/// - 5 Locked Design Decisions in docs/folder-grouping-implementation-plan.html:216-239
///   (first-child granularity only; Hybrid persistence; bytes primary for progress/counts;
///    full requeue support; opaque + recursive download).
/// - Recommended Persistence Model — Hybrid (plan.html:281-290).
/// - Concrete Example tree (plan.html:244-263) — the fake must reproduce this exactly.
/// - Current State (plan.html:267-277) — no real SFTP yet; only dev sim with flat INSERTs.
/// - Column spec (locked by sibling Subagent A; use these exact names/types/defaults):
///   is_group boolean NOT NULL DEFAULT false,
///   group_remote_path varchar(2000) NULL,
///   child_count integer NOT NULL DEFAULT 0
///   (child_count = total leaf files in subtree for groups (0 otherwise);
///    group_remote_path on leaves points back to the group's remote_path (always ends with '/');
///    group rows: group_remote_path=NULL, remote_path ends with '/';
///    indexes on (sync_run_id, is_group) and (job_id, group_remote_path);
///    comments explain dual-use aggregates on group rows with bytes primary).
/// - Authoritative grouping algorithm: (future) docs/grouping-rules.md (single source of truth;
///   key excerpts provided by sibling A: first-child granularity only on normalized sourcePath;
///   Hybrid creation of visible group row (is_group=true, group_remote_path=NULL, remote_path ends /,
///   child_count=total leaves, file_size_bytes=subtree sum) + ALL descendant leaf rows
///   (is_group=false, group_remote_path=group's value, child_count=0);
///   bytes via STAT/LIST at scan + recursive sum for groups; empty immediate child dirs still
///   get group row (0/0); requeue resets group + leaves via WHERE group_remote_path=...;
///   run total_file_count at enqueue = # visible first-child entries (groups+looses), not SUM(child_count);
///   invariants listed at end of rules doc; trailing-/ convention for group remote_path).
/// - Phase 4 verbatim (plan.html:340-348): "Define a clean interface (e.g. ISftpDirectoryScanner...)
///   that the future real worker will call. The contract must accept a job's sourcePath and return
///   a list of top-level 'first child' entries (folders marked as groups + loose files). For each
///   group entry, the implementation will later recursively list and create the leaf rows linked
///   to the group. Define how byte sizes are obtained during initial scan (SFTP STAT or LIST with size)
///   and how they flow into both group and leaf rows. Document the exact grouping algorithm in comments
///   + the plan so the future implementer cannot misinterpret 'first child'." "This phase can be a
///   pure interface + unit-testable fake implementation first; the real SSH.NET / SFTP.NET client
///   work can come in a follow-up slice."
/// </para>
/// <para>
/// First-child determination (per rules + locked #1): normalize sourcePath (exact rules in grouping-rules.md;
///   current jobs often lack trailing / — e.g. DevelopmentSimulationService.cs:69 '/remote/incoming',
///   tests use '/incoming' — interface + fake must handle both identically); use STAT (or LIST entry type)
///   to distinguish file vs dir at the direct children only; groups always get trailing '/' in remote_path;
///   deeper nesting never appears in VisibleEntries.
/// </para>
/// <para>
/// Byte size flow (locked #3 + column spec + Phase 4): sizes from SFTP STAT/LIST on files at scan time;
///   groups receive recursive sum of all leaf file sizes in subtree (no dir "sizes"); flows into
///   FileSizeBytes on both the group row and (for visibility) the run total_bytes; child_count is
///   secondary (total leaves, not primary count).
/// </para>
/// <para>
/// Hybrid creation (locked #2): VisibleEntries become the UI-facing rows (is_group per entry.IsGroup,
///   group_remote_path=NULL for both groups and loose files); InternalLeafEntries are the hidden
///   descendants (is_group=false, group_remote_path set, child_count=0) created by the worker via
///   repository INSERT after this call. Requeue support (locked #4) uses the persisted links
///   (WHERE group_remote_path = ...); scanner itself is not called for requeue.
/// </para>
/// <para>
/// Typical future worker usage (Phase 0 scope only): given runId + job (with its SourcePath/DestinationPath)
///   + run record, call ScanFirstLevelAsync, then INSERT all VisibleEntries + InternalLeafEntries
///   (setting job_id, sync_run_id, status=queued, timestamps, etc.; using the pre-computed remote/dest/size/child/isGroup/groupRemote
///   values) + initialize run totals from the summary fields (VisibleEntries.Count for total_file_count
///   per rules, TotalBytes for byte aggregates). All public APIs remain unchanged.
/// </para>
/// <para>
/// Fully unit-testable today with no SSH.NET, no SFTP, no network, no DB (see FakeSftpDirectoryScanner
///   skeleton + proposed tests below). The real implementation will depend on an SFTP client (injected
///   later) but must produce identical entry structure, aggregates, linking, and visible/internal split
///   for any given tree.
/// </para>
/// </remarks>
public interface ISftpDirectoryScanner
{
    /// <summary>
    /// Scans only the immediate direct (first-child) children under the normalized sourcePath.
    /// Returns the visible first-child entries (groups + loose files) plus all internal leaf rows
    /// required for hybrid persistence, plus summary aggregates for run initialization.
    /// </summary>
    /// <param name="sourcePath">Job's sourcePath (with or without trailing /; normalized per grouping-rules.md).</param>
    /// <param name="destinationPath">Job's destinationPath (used to compute relative DestinationPath for every entry).</param>
    /// <param name="cancellationToken">Standard cancellation; passed through to any underlying operations.</param>
    /// <returns>
    /// FirstLevelScanResult containing exactly the structure needed to perform a hybrid enqueue
    /// for the run (VisibleEntries for UI-facing rows, InternalLeafEntries for linked leaves under groups).
    /// Never leaks internal leaves into VisibleEntries (per locked #1 + rules invariants).
    /// </returns>
    /// <remarks>
    /// Byte sizes obtained via STAT/LIST (or simulated equivalent) at scan time + recursive sum for groups.
    /// This single method encapsulates the "recursive list" for groups inside the implementation
    /// (fake today, real SFTP client later) so the caller (worker) cannot misinterpret first-child.
    /// See full interface XML remarks for locked decisions, column spec, and rules.md reference.
    /// </remarks>
    Task<FirstLevelScanResult> ScanFirstLevelAsync(
        string sourcePath,
        string destinationPath,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// A single scanned entry: either a visible first-child (group or loose file) or an internal leaf.
/// Fields map directly to the locked columns + existing DownloadQueueItem columns (added in Phase 1/3).
/// </summary>
public sealed record ScannedRemoteEntry(
    string RemotePath,              // full remote (groups always end with '/'; per trailing-/ convention in rules.md)
    string DestinationPath,         // relative under the provided destinationPath base
    bool IsGroup,                   // true for visible opaque groups; false for loose files and all internal leaves
    long FileSizeBytes,             // subtree sum for groups (bytes primary per locked #3 + column spec); file size for leaves/loose
    int ChildCount,                 // total leaf files in subtree for groups (0 otherwise; per locked column spec from sibling A)
    string? GroupRemotePath,        // for internal leaves: the group's remote_path value (ends '/'); null for visible groups + loose files
    DateTimeOffset? RemoteModifiedAt // leaf: from STAT mtime; group: MAX mtime across subtree leaves (or NULL per grouping-rules.md:102) — or unavailable
);

/// <summary>
/// Result of a first-level scan, ready for the worker to drive a hybrid enqueue + run initialization.
/// </summary>
public sealed record FirstLevelScanResult(
    /// <summary>
    /// Visible first-child entries only (immediate direct children under normalized sourcePath).
    /// These become the UI-facing download_queue_items rows (is_group = IsGroup, group_remote_path = NULL).
    /// Length of this list = run total_file_count at enqueue time (per rules excerpts, not SUM(child_count)).
    /// Never contains any internal leaves (enforces opaque first-child granularity).
    /// </summary>
    IReadOnlyList<ScannedRemoteEntry> VisibleEntries,

    /// <summary>
    /// All descendant leaf file rows for the groups present in VisibleEntries (hybrid model).
    /// Each has IsGroup=false, ChildCount=0, GroupRemotePath set to the owning group's RemotePath (ends '/').
    /// Worker INSERTs these linked to the group after creating the group row. Empty when no groups or all groups empty.
    /// </summary>
    IReadOnlyList<ScannedRemoteEntry> InternalLeafEntries,

    /// <summary>
    /// Total bytes across all visible entries (groups use their subtree sums; loose use file size).
    /// Use for run.total_bytes initialization (bytes primary per locked #3).
    /// </summary>
    long TotalBytes,

    /// <summary>
    /// Count of groups in VisibleEntries (for run init / reporting).
    /// </summary>
    int VisibleGroupCount,

    /// <summary>
    /// Count of loose files in VisibleEntries (for run init / reporting).
    /// </summary>
    int VisibleLooseFileCount
);
