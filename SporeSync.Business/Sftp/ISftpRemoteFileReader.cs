namespace SporeSync.Business.Sftp;

/// <summary>
/// Minimal read-side abstraction over a connected SFTP client so download/resume/integrity
/// logic can be unit tested without a real SFTP connection.
/// </summary>
public interface ISftpRemoteFileReader
{
    /// <summary>Returns the current size and modification time of the remote file.</summary>
    SftpRemoteFileInfo GetFileInfo(string remotePath);

    /// <summary>Opens a (seekable where supported) read stream over the remote file.</summary>
    Stream OpenRead(string remotePath);
}

public readonly record struct SftpRemoteFileInfo(long Length, DateTimeOffset? ModifiedAt);

internal sealed class SftpClientRemoteFileReader : ISftpRemoteFileReader
{
    private readonly Renci.SshNet.SftpClient _client;

    public SftpClientRemoteFileReader(Renci.SshNet.SftpClient client)
    {
        _client = client;
    }

    public SftpRemoteFileInfo GetFileInfo(string remotePath)
    {
        var attributes = _client.GetAttributes(remotePath);
        return new SftpRemoteFileInfo(attributes.Size, ToModifiedAt(attributes.LastWriteTimeUtc));
    }

    public Stream OpenRead(string remotePath) => _client.OpenRead(remotePath);

    private static DateTimeOffset? ToModifiedAt(DateTime lastWriteTimeUtc)
    {
        if (lastWriteTimeUtc == DateTime.MinValue)
        {
            return null;
        }

        return new DateTimeOffset(DateTime.SpecifyKind(lastWriteTimeUtc, DateTimeKind.Utc));
    }
}
