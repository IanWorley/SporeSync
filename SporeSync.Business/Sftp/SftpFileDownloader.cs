using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Renci.SshNet;
using SporeSync.Business.Security;

namespace SporeSync.Business.Sftp;

public sealed class SftpFileDownloader
{
    private readonly ISftpClientFactory _clientFactory;
    private readonly LocalDestinationPathSandbox _destinationPathSandbox;
    private readonly ILogger<SftpFileDownloader> _logger;

    public SftpFileDownloader(
        ISftpClientFactory clientFactory,
        LocalDestinationPathSandbox destinationPathSandbox,
        ILogger<SftpFileDownloader> logger)
    {
        _clientFactory = clientFactory;
        _destinationPathSandbox = destinationPathSandbox;
        _logger = logger;
    }

    public async Task<SftpDownloadResult> DownloadAsync(
        Guid connectionProfileId,
        string remotePath,
        string localPath,
        IProgress<long>? progress = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            localPath = _destinationPathSandbox.RequireContainedPath(localPath, nameof(localPath));
        }
        catch (ArgumentException ex)
        {
            _logger.LogWarning(
                ex,
                "Refusing to download {RemotePath} to unsafe local path {LocalPath}",
                remotePath,
                localPath);
            return new SftpDownloadResult(false, 0, null, ex.Message);
        }

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
            const int bufferSize = 81920;
            var buffer = new byte[bufferSize];
            long totalRead = 0;
            int bytesRead;
            var lastReportSw = Stopwatch.StartNew();
            while ((bytesRead = await remoteStream.ReadAsync(buffer.AsMemory(0, bufferSize), cancellationToken)) > 0)
            {
                await localStream.WriteAsync(buffer.AsMemory(0, bytesRead), cancellationToken);
                totalRead += bytesRead;
                if (progress is not null && lastReportSw.ElapsedMilliseconds >= 150)
                {
                    progress.Report(totalRead);
                    lastReportSw.Restart();
                }
            }
            if (progress is not null)
            {
                progress.Report(totalRead);
            }
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
