// using System;
// using Microsoft.Extensions.Logging;
// using SporeSync.Domain.Interfaces;
// using SporeSync.Domain.Models;

// namespace SporeSync.Application.Services;

// public class SyncService
// {
//     private readonly ISshService _sshService;
//     private readonly IQueueService _queueService;
//     private readonly ILogger<SyncService> _logger;

//     public SyncService(ISshService sshService, IQueueService queueService, ILogger<SyncService> logger)
//     {
//         _sshService = sshService;
//         _queueService = queueService;
//         _logger = logger;
//     }

//     public async Task<bool> SyncDirectoryAsync(string remotePath, string localPath,
//         IProgress<UploadProgress>? progress = null, CancellationToken cancellationToken = default)
//     {
//         _logger.LogInformation("Starting directory sync from {RemotePath} to {LocalPath}", remotePath, localPath);

//         try
//         {
//             // Download the entire directory
//             var success = await _sshService.DownloadDirectoryAsync(remotePath, localPath, progress, cancellationToken);

//             if (success)
//             {
//                 _logger.LogInformation("Directory sync completed successfully from {RemotePath} to {LocalPath}", remotePath, localPath);
//                 // Queue any post-sync operations if needed
//                 // await _queueService.EnqueueSyncAsync(new TrackedItem { ... });
//             }
//             else
//             {
//                 _logger.LogWarning("Directory sync failed from {RemotePath} to {LocalPath}", remotePath, localPath);
//             }

//             return success;
//         }
//         catch (Exception ex)
//         {
//             _logger.LogError(ex, "Exception occurred during directory sync from {RemotePath} to {LocalPath}", remotePath, localPath);
//             return false;
//         }
//     }

//     public async Task<IEnumerable<RemoteFileInfo>> GetRemoteFilesAsync(string remotePath)
//     {
//         _logger.LogDebug("Getting remote files from {RemotePath}", remotePath);
//         return await _sshService.ListFilesAsync(remotePath);
//     }

//     public async Task<bool> SyncSingleFileAsync(string remotePath, string localPath,
//         IProgress<UploadProgress>? progress = null, CancellationToken cancellationToken = default)
//     {
//         _logger.LogInformation("Starting single file sync from {RemotePath} to {LocalPath}", remotePath, localPath);

//         try
//         {
//             var success = await _sshService.DownloadFileAsync(remotePath, localPath, progress, cancellationToken);

//             if (success)
//             {
//                 _logger.LogInformation("Single file sync completed successfully from {RemotePath} to {LocalPath}", remotePath, localPath);
//             }
//             else
//             {
//                 _logger.LogWarning("Single file sync failed from {RemotePath} to {LocalPath}", remotePath, localPath);
//             }

//             return success;
//         }
//         catch (Exception ex)
//         {
//             _logger.LogError(ex, "Exception occurred during single file sync from {RemotePath} to {LocalPath}", remotePath, localPath);
//             return false;
//         }
//     }
// }
