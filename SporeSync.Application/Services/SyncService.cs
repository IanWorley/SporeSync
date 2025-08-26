using System;
using SporeSync.Domain.Interfaces;
using SporeSync.Domain.Models;

namespace SporeSync.Application.Services;

public class SyncService
{
    private readonly ISshService _sshService;
    private readonly IQueueService _queueService;

    public SyncService(ISshService sshService, IQueueService queueService)
    {
        _sshService = sshService;
        _queueService = queueService;
    }

    public async Task<bool> SyncDirectoryAsync(string remotePath, string localPath,
        IProgress<UploadProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        try
        {
            // Test connection first
            if (!await _sshService.TestConnectionAsync())
            {
                throw new InvalidOperationException("SSH connection is not available");
            }

            // Download the entire directory
            var success = await _sshService.DownloadDirectoryAsync(remotePath, localPath, progress, cancellationToken);

            if (success)
            {
                // Queue any post-sync operations if needed
                // await _queueService.EnqueueSyncAsync(new TrackedItem { ... });
            }

            return success;
        }
        catch (Exception)
        {
            return false;
        }
    }

    public async Task<IEnumerable<RemoteFileInfo>> GetRemoteFilesAsync(string remotePath)
    {
        return await _sshService.ListFilesAsync(remotePath);
    }

    public async Task<bool> SyncSingleFileAsync(string remotePath, string localPath,
        IProgress<UploadProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        try
        {
            return await _sshService.DownloadFileAsync(remotePath, localPath, progress, cancellationToken);
        }
        catch (Exception)
        {
            return false;
        }
    }

    public SshConnectionState GetConnectionState()
    {
        return _sshService.GetConnectionState();
    }
}
