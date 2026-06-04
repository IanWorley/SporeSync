using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Renci.SshNet;
using SporeSync.Business.Sftp;

namespace SporeSync.Business.Sftp;

public sealed class SftpFileDownloader
{
    private readonly ISftpClientFactory _clientFactory;
    private readonly ILogger<SftpFileDownloader> _logger;

    public SftpFileDownloader(
        ISftpClientFactory clientFactory,
        ILogger<SftpFileDownloader> logger)
    {
        _clientFactory = clientFactory;
        _logger = logger;
    }

    public async Task<SftpDownloadResult> DownloadAsync(
        Guid connectionProfileId,
        string remotePath,
        string localPath,
        CancellationToken cancellationToken = default)
    {
        await using var connected = await _clientFactory.ConnectAsync(connectionProfileId, cancellationToken);
        var client = connected.Client;

        var directory = Path.GetDirectoryName(localPath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var tempPath = localPath + ".part";
        if (File.Exists(tempPath))
        {
            File.Delete(tempPath);
        }

        var stopwatch = Stopwatch.StartNew();
        try
        {
            await using var remoteStream = client.OpenRead(remotePath);
            await using var localStream = File.Create(tempPath);
            await remoteStream.CopyToAsync(localStream, cancellationToken);
            localStream.Flush(true);
        }
        catch (Exception ex)
        {
            if (File.Exists(tempPath))
            {
                File.Delete(tempPath);
            }

            _logger.LogWarning(ex, "Failed to download {RemotePath} to {LocalPath}", remotePath, localPath);
            return new SftpDownloadResult(false, 0, null, ex.Message);
        }

        stopwatch.Stop();
        File.Move(tempPath, localPath, overwrite: true);

        var fileInfo = new FileInfo(localPath);
        var bytesPerSecond = stopwatch.Elapsed.TotalSeconds > 0
            ? (decimal)(fileInfo.Length / stopwatch.Elapsed.TotalSeconds)
            : (decimal?)fileInfo.Length;

        return new SftpDownloadResult(true, fileInfo.Length, bytesPerSecond, null);
    }
}

public sealed record SftpDownloadResult(
    bool Success,
    long BytesDownloaded,
    decimal? BytesPerSecond,
    string? ErrorMessage);
