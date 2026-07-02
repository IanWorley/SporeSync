using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SporeSync.Business.Security;

namespace SporeSync.Business.Sftp;

public interface ISftpFileDownloader
{
    Task<SftpDownloadResult> DownloadAsync(
        Guid connectionProfileId,
        string remotePath,
        string localPath,
        CancellationToken cancellationToken = default);

    Task<SftpDownloadResult> DownloadAsync(
        Guid connectionProfileId,
        string remotePath,
        string localPath,
        IProgress<long>? progress,
        CancellationToken cancellationToken = default);
}

public sealed class SftpFileDownloader : ISftpFileDownloader
{
    private readonly ISftpClientFactory _clientFactory;
    private readonly LocalDestinationPathSandbox _destinationPathSandbox;
    private readonly SporeSyncOptions _options;
    private readonly ILogger<SftpFileDownloader> _logger;

    public SftpFileDownloader(
        ISftpClientFactory clientFactory,
        LocalDestinationPathSandbox destinationPathSandbox,
        IOptions<SporeSyncOptions> options,
        ILogger<SftpFileDownloader> logger)
    {
        _clientFactory = clientFactory;
        _destinationPathSandbox = destinationPathSandbox;
        _options = options.Value;
        _logger = logger;
    }

    public Task<SftpDownloadResult> DownloadAsync(
        Guid connectionProfileId,
        string remotePath,
        string localPath,
        CancellationToken cancellationToken = default) =>
        DownloadAsync(connectionProfileId, remotePath, localPath, progress: null, cancellationToken);

    public async Task<SftpDownloadResult> DownloadAsync(
        Guid connectionProfileId,
        string remotePath,
        string localPath,
        IProgress<long>? progress,
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
            return SftpDownloadResult.Failure(ex.Message);
        }

        await using var connected = await _clientFactory.ConnectAsync(connectionProfileId, cancellationToken);

        return await DownloadWithReaderAsync(
            new SftpClientRemoteFileReader(connected.Client),
            remotePath,
            localPath,
            progress,
            cancellationToken);
    }

    /// <summary>
    /// Core download pipeline (stability window, .part resume by offset, post-transfer size
    /// verification). Public so tests can exercise it with a fake reader; production callers
    /// go through <see cref="DownloadAsync"/> which applies the destination sandbox and connects.
    /// </summary>
    public async Task<SftpDownloadResult> DownloadWithReaderAsync(
        ISftpRemoteFileReader remote,
        string remotePath,
        string localPath,
        IProgress<long>? progress = null,
        CancellationToken cancellationToken = default)
    {
        SftpRemoteFileInfo remoteInfo;
        try
        {
            remoteInfo = remote.GetFileInfo(remotePath);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Failed to stat remote file {RemotePath}", remotePath);
            return SftpDownloadResult.Failure(ex.Message);
        }

        // Stability window: a remote file modified moments ago is likely still being uploaded.
        // Defer instead of downloading a half-written file (does not consume retry budget).
        var stabilityWindow = TimeSpan.FromSeconds(Math.Max(0, _options.RemoteFileStabilityWindowSeconds));
        if (stabilityWindow > TimeSpan.Zero && remoteInfo.ModifiedAt is { } modifiedAt)
        {
            var age = DateTimeOffset.UtcNow - modifiedAt;
            if (age < stabilityWindow)
            {
                return SftpDownloadResult.Defer(
                    $"Remote file '{remotePath}' was modified {Math.Max(0, (int)age.TotalSeconds)}s ago " +
                    $"and may still be uploading; deferring until it is stable for {(int)stabilityWindow.TotalSeconds}s.");
            }
        }

        var directory = Path.GetDirectoryName(localPath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var tempPath = localPath + ".part";
        var offset = DetermineResumeOffset(tempPath, remoteInfo);
        if (offset > 0)
        {
            _logger.LogInformation(
                "Resuming download of {RemotePath} from offset {Offset} of {TotalBytes} bytes",
                remotePath,
                offset,
                remoteInfo.Length);
        }

        var stopwatch = Stopwatch.StartNew();
        try
        {
            if (offset < remoteInfo.Length)
            {
                await using var remoteStream = remote.OpenRead(remotePath);
                if (offset > 0)
                {
                    if (remoteStream.CanSeek)
                    {
                        remoteStream.Seek(offset, SeekOrigin.Begin);
                    }
                    else
                    {
                        offset = 0;
                    }
                }

                await using var localStream = new FileStream(
                    tempPath,
                    offset > 0 ? FileMode.Append : FileMode.Create,
                    FileAccess.Write);

                const int bufferSize = 81920;
                var buffer = new byte[bufferSize];
                var totalRead = offset;
                if (totalRead > 0)
                {
                    progress?.Report(totalRead);
                }

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

                progress?.Report(totalRead);
                localStream.Flush(true);
            }
            else if (!File.Exists(tempPath))
            {
                // Zero-byte remote file with nothing to copy: materialize an empty temp file.
                await using var _ = File.Create(tempPath);
                progress?.Report(0);
            }
            else
            {
                progress?.Report(remoteInfo.Length);
            }
        }
        catch (OperationCanceledException)
        {
            StampPartWithRemoteTimestamp(tempPath, remoteInfo);
            throw;
        }
        catch (Exception ex)
        {
            // Intentionally keep the .part file: a later attempt can resume from this offset.
            StampPartWithRemoteTimestamp(tempPath, remoteInfo);
            _logger.LogWarning(ex, "Failed to download {RemotePath} to {LocalPath}", remotePath, localPath);
            return SftpDownloadResult.Failure(ex.Message);
        }

        stopwatch.Stop();

        // Integrity: verify the byte count on disk matches what the server reported before
        // the transfer started.
        var actualLength = new FileInfo(tempPath).Length;
        if (actualLength != remoteInfo.Length)
        {
            if (actualLength > remoteInfo.Length)
            {
                // Local part is larger than the remote file; it cannot be a valid prefix.
                File.Delete(tempPath);
            }
            else
            {
                // Short part kept for resume.
                StampPartWithRemoteTimestamp(tempPath, remoteInfo);
            }

            _logger.LogWarning(
                "Size verification failed for {RemotePath}: expected {ExpectedBytes} bytes but received {ActualBytes}",
                remotePath,
                remoteInfo.Length,
                actualLength);
            return SftpDownloadResult.Failure(
                $"Size verification failed for '{remotePath}': expected {remoteInfo.Length} bytes but received {actualLength}.");
        }

        File.Move(tempPath, localPath, overwrite: true);

        var transferredBytes = remoteInfo.Length - offset;
        var bytesPerSecond = stopwatch.Elapsed.TotalSeconds > 0
            ? (decimal)(transferredBytes / stopwatch.Elapsed.TotalSeconds)
            : (decimal?)transferredBytes;

        return SftpDownloadResult.Succeed(remoteInfo.Length, bytesPerSecond);
    }

    /// <summary>
    /// Stamps a kept .part file with the remote modification time observed when this attempt
    /// started. Without this, a remote file modified *during* the attempt would still look older
    /// than the part (whose filesystem mtime is "now"), and a later attempt would resume onto a
    /// mix of old and new content. With the stamp, a mid-attempt remote change yields a remote
    /// mtime strictly newer than the part, so <see cref="DetermineResumeOffset"/> discards it.
    /// </summary>
    private static void StampPartWithRemoteTimestamp(string tempPath, SftpRemoteFileInfo remoteInfo)
    {
        if (remoteInfo.ModifiedAt is not { } modifiedAt || !File.Exists(tempPath))
        {
            return;
        }

        try
        {
            File.SetLastWriteTimeUtc(tempPath, modifiedAt.UtcDateTime);
        }
        catch (IOException)
        {
            // Best effort: a failed stamp only makes the resume check more conservative later.
        }
    }

    /// <summary>
    /// Decides whether an existing .part file can be resumed. The part must be a plausible prefix
    /// of the current remote file: non-empty, no larger than the remote file, and written after the
    /// remote file's last modification (otherwise the remote content may have changed under us).
    /// </summary>
    private static long DetermineResumeOffset(string tempPath, SftpRemoteFileInfo remoteInfo)
    {
        var partInfo = new FileInfo(tempPath);
        if (!partInfo.Exists)
        {
            return 0;
        }

        var partWrittenAt = new DateTimeOffset(DateTime.SpecifyKind(partInfo.LastWriteTimeUtc, DateTimeKind.Utc));
        var resumable = partInfo.Length > 0
            && partInfo.Length <= remoteInfo.Length
            && remoteInfo.ModifiedAt is { } remoteModifiedAt
            && remoteModifiedAt <= partWrittenAt;

        if (!resumable)
        {
            partInfo.Delete();
            return 0;
        }

        return partInfo.Length;
    }
}

public sealed record SftpDownloadResult(
    bool Success,
    long BytesDownloaded,
    decimal? BytesPerSecond,
    string? ErrorMessage,
    bool Deferred = false)
{
    public static SftpDownloadResult Succeed(long bytesDownloaded, decimal? bytesPerSecond) =>
        new(true, bytesDownloaded, bytesPerSecond, null);

    public static SftpDownloadResult Failure(string? errorMessage) =>
        new(false, 0, null, errorMessage);

    public static SftpDownloadResult Defer(string reason) =>
        new(false, 0, null, reason, Deferred: true);
}
