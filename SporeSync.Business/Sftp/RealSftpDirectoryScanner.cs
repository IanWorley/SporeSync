using SporeSync.Business.Scanning;
using SporeSync.Business.Sftp;

namespace SporeSync.Business.Sftp;

public sealed class RealSftpDirectoryScanner
{
    private readonly ISftpClientFactory _clientFactory;

    public RealSftpDirectoryScanner(ISftpClientFactory clientFactory)
    {
        _clientFactory = clientFactory;
    }

    public async Task<FirstLevelScanResult> ScanFirstLevelAsync(
        Guid connectionProfileId,
        string sourcePath,
        string destinationPath,
        CancellationToken cancellationToken = default)
    {
        await using var connected = await _clientFactory.ConnectAsync(connectionProfileId, cancellationToken);
        var client = connected.Client;

        var normalizedSource = NormalizeDirectoryPath(sourcePath);
        var normalizedDestination = NormalizeDirectoryPath(destinationPath);

        if (IsFilePath(sourcePath, client))
        {
            var attributes = client.GetAttributes(sourcePath);
            var entry = new ScannedRemoteEntry(
                sourcePath,
                destinationPath,
                IsGroup: false,
                FileSizeBytes: attributes.Size,
                ChildCount: 0,
                GroupRemotePath: null,
                RemoteModifiedAt: ToModifiedAt(attributes));
            return new FirstLevelScanResult(
                VisibleEntries: new[] { entry },
                InternalLeafEntries: Array.Empty<ScannedRemoteEntry>(),
                TotalBytes: entry.FileSizeBytes,
                VisibleGroupCount: 0,
                VisibleLooseFileCount: 1);
        }

        var visible = new List<ScannedRemoteEntry>();
        var leaves = new List<ScannedRemoteEntry>();
        long totalBytes = 0;
        var groupCount = 0;
        var looseCount = 0;

        foreach (var child in ListImmediateChildren(client, normalizedSource).OrderBy(c => c.Name, StringComparer.Ordinal))
        {
            var childRemote = normalizedSource + child.Name + (child.IsDirectory ? "/" : string.Empty);
            var childDest = normalizedDestination + child.Name + (child.IsDirectory ? "/" : string.Empty);

            if (child.IsDirectory)
            {
                var subtreeLeaves = CollectLeavesUnder(
                    client,
                    childRemote,
                    normalizedSource,
                    normalizedDestination);
                var subtreeBytes = subtreeLeaves.Sum(leaf => leaf.FileSizeBytes);
                var maxMtime = subtreeLeaves.Count > 0
                    ? subtreeLeaves.Max(leaf => leaf.RemoteModifiedAt)
                    : null;

                visible.Add(new ScannedRemoteEntry(
                    childRemote,
                    childDest,
                    IsGroup: true,
                    FileSizeBytes: subtreeBytes,
                    ChildCount: subtreeLeaves.Count,
                    GroupRemotePath: null,
                    RemoteModifiedAt: maxMtime));
                leaves.AddRange(subtreeLeaves);
                totalBytes += subtreeBytes;
                groupCount++;
            }
            else
            {
                visible.Add(new ScannedRemoteEntry(
                    childRemote,
                    childDest,
                    IsGroup: false,
                    FileSizeBytes: child.Size,
                    ChildCount: 0,
                    GroupRemotePath: null,
                    RemoteModifiedAt: child.ModifiedAt));
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

    private static bool IsFilePath(string sourcePath, Renci.SshNet.SftpClient client)
    {
        var attributes = client.GetAttributes(sourcePath);
        return !attributes.IsDirectory;
    }

    private static IEnumerable<(string Name, bool IsDirectory, long Size, DateTimeOffset? ModifiedAt)> ListImmediateChildren(
        Renci.SshNet.SftpClient client,
        string normalizedDirectory)
    {
        foreach (var entry in client.ListDirectory(normalizedDirectory))
        {
            if (entry.Name is "." or "..")
            {
                continue;
            }

            yield return (
                entry.Name,
                entry.IsDirectory,
                entry.Attributes.Size,
                ToModifiedAt(entry.Attributes));
        }
    }

    private static List<ScannedRemoteEntry> CollectLeavesUnder(
        Renci.SshNet.SftpClient client,
        string groupRemotePath,
        string normalizedSource,
        string normalizedDestination)
    {
        var collected = new List<ScannedRemoteEntry>();
        WalkDirectory(client, groupRemotePath, normalizedSource, normalizedDestination, groupRemotePath, collected);
        return collected;
    }

    private static void WalkDirectory(
        Renci.SshNet.SftpClient client,
        string currentRemotePath,
        string normalizedSource,
        string normalizedDestination,
        string groupRemotePath,
        List<ScannedRemoteEntry> collected)
    {
        foreach (var entry in client.ListDirectory(currentRemotePath))
        {
            if (entry.Name is "." or "..")
            {
                continue;
            }

            var remotePath = currentRemotePath.EndsWith('/')
                ? currentRemotePath + entry.Name
                : currentRemotePath + "/" + entry.Name;

            if (entry.IsDirectory)
            {
                var nextRemote = remotePath + "/";
                WalkDirectory(client, nextRemote, normalizedSource, normalizedDestination, groupRemotePath, collected);
                continue;
            }

            var relative = remotePath.Substring(normalizedSource.Length);
            collected.Add(new ScannedRemoteEntry(
                remotePath,
                normalizedDestination + relative,
                IsGroup: false,
                FileSizeBytes: entry.Attributes.Size,
                ChildCount: 0,
                GroupRemotePath: groupRemotePath,
                RemoteModifiedAt: ToModifiedAt(entry.Attributes)));
        }
    }

    private static DateTimeOffset? ToModifiedAt(Renci.SshNet.Sftp.SftpFileAttributes attributes)
    {
        var utc = attributes.LastWriteTimeUtc;
        if (utc == DateTime.MinValue)
        {
            return null;
        }

        return new DateTimeOffset(DateTime.SpecifyKind(utc, DateTimeKind.Utc));
    }

    private static string NormalizeDirectoryPath(string path)
    {
        var trimmed = (path ?? string.Empty).Trim();
        return trimmed.EndsWith('/') ? trimmed : trimmed + "/";
    }
}
