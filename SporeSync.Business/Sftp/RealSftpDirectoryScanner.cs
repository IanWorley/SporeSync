using Renci.SshNet.Sftp;
using SporeSync.Business.Scanning;

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

        var source = await client.GetAsync(sourcePath, cancellationToken);
        if (!source.IsDirectory)
        {
            var entry = new ScannedRemoteEntry(
                sourcePath,
                destinationPath,
                IsGroup: false,
                FileSizeBytes: source.Length,
                ChildCount: 0,
                GroupRemotePath: null,
                RemoteModifiedAt: ToModifiedAt(source.LastWriteTimeUtc));
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

        var children = await ListChildrenAsync(client, normalizedSource, cancellationToken);
        foreach (var child in children.OrderBy(c => c.Name, StringComparer.Ordinal))
        {
            var childRemote = normalizedSource + child.Name + (child.IsDirectory ? "/" : string.Empty);
            var childDest = normalizedDestination + child.Name + (child.IsDirectory ? "/" : string.Empty);

            if (child.IsDirectory)
            {
                var subtreeLeaves = new List<ScannedRemoteEntry>();
                await WalkDirectoryAsync(
                    client,
                    childRemote,
                    normalizedSource,
                    normalizedDestination,
                    childRemote,
                    subtreeLeaves,
                    cancellationToken);
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
                    FileSizeBytes: child.Length,
                    ChildCount: 0,
                    GroupRemotePath: null,
                    RemoteModifiedAt: ToModifiedAt(child.LastWriteTimeUtc)));
                totalBytes += child.Length;
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

    private static async Task<List<ISftpFile>> ListChildrenAsync(
        Renci.SshNet.SftpClient client,
        string directoryPath,
        CancellationToken cancellationToken)
    {
        var children = new List<ISftpFile>();
        await foreach (var entry in client.ListDirectoryAsync(directoryPath, cancellationToken))
        {
            if (entry.Name is "." or "..")
            {
                continue;
            }

            children.Add(entry);
        }

        return children;
    }

    private static async Task WalkDirectoryAsync(
        Renci.SshNet.SftpClient client,
        string currentRemotePath,
        string normalizedSource,
        string normalizedDestination,
        string groupRemotePath,
        List<ScannedRemoteEntry> collected,
        CancellationToken cancellationToken)
    {
        foreach (var entry in await ListChildrenAsync(client, currentRemotePath, cancellationToken))
        {
            var remotePath = currentRemotePath.EndsWith('/')
                ? currentRemotePath + entry.Name
                : currentRemotePath + "/" + entry.Name;

            if (entry.IsDirectory)
            {
                var nextRemote = remotePath + "/";
                await WalkDirectoryAsync(
                    client,
                    nextRemote,
                    normalizedSource,
                    normalizedDestination,
                    groupRemotePath,
                    collected,
                    cancellationToken);
                continue;
            }

            var relative = remotePath.Substring(normalizedSource.Length);
            collected.Add(new ScannedRemoteEntry(
                remotePath,
                normalizedDestination + relative,
                IsGroup: false,
                FileSizeBytes: entry.Length,
                ChildCount: 0,
                GroupRemotePath: groupRemotePath,
                RemoteModifiedAt: ToModifiedAt(entry.LastWriteTimeUtc)));
        }
    }

    private static DateTimeOffset? ToModifiedAt(DateTime lastWriteTimeUtc)
    {
        if (lastWriteTimeUtc == DateTime.MinValue)
        {
            return null;
        }

        return new DateTimeOffset(DateTime.SpecifyKind(lastWriteTimeUtc, DateTimeKind.Utc));
    }

    private static string NormalizeDirectoryPath(string path)
    {
        var trimmed = (path ?? string.Empty).Trim();
        return trimmed.EndsWith('/') ? trimmed : trimmed + "/";
    }
}
