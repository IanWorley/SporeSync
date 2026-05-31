using Renci.SshNet.Common;
using SporeSync.Business.Interface;
using SporeSync.Business.Sftp;
using SporeSync.Domain.Interface;

namespace SporeSync.Business.Service;

public sealed class DownloadQueueItemFileDeleteService : IDownloadQueueItemFileDeleteService
{
    private readonly IDownloadQueueItemRepository _queueItemRepository;
    private readonly ISporeSyncJobRepository _jobRepository;
    private readonly ISftpClientFactory _clientFactory;

    public DownloadQueueItemFileDeleteService(
        IDownloadQueueItemRepository queueItemRepository,
        ISporeSyncJobRepository jobRepository,
        ISftpClientFactory clientFactory)
    {
        _queueItemRepository = queueItemRepository;
        _jobRepository = jobRepository;
        _clientFactory = clientFactory;
    }

    public async Task<DeleteQueueItemFileResult> DeleteLocalAsync(
        Guid runId,
        Guid queueItemId,
        CancellationToken cancellationToken = default)
    {
        var item = await _queueItemRepository.GetByIdAsync(queueItemId, cancellationToken);
        if (item is null || item.SyncRunId != runId)
        {
            return NotFound(queueItemId, "local");
        }

        try
        {
            var existed = DeleteLocalPath(item.DestinationPath, item.IsGroup);
            return new DeleteQueueItemFileResult(
                DeleteQueueItemFileStatus.Deleted,
                item.Id,
                "local",
                item.DestinationPath,
                existed);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return new DeleteQueueItemFileResult(
                DeleteQueueItemFileStatus.Failed,
                item.Id,
                "local",
                item.DestinationPath,
                Existed: true,
                ex.Message);
        }
    }

    public async Task<DeleteQueueItemFileResult> DeleteRemoteAsync(
        Guid runId,
        Guid queueItemId,
        CancellationToken cancellationToken = default)
    {
        var item = await _queueItemRepository.GetByIdAsync(queueItemId, cancellationToken);
        if (item is null || item.SyncRunId != runId)
        {
            return NotFound(queueItemId, "remote");
        }

        var job = await _jobRepository.GetByIdAsync(item.JobId, cancellationToken);
        if (job is null)
        {
            return new DeleteQueueItemFileResult(
                DeleteQueueItemFileStatus.JobNotFound,
                item.Id,
                "remote",
                item.RemotePath,
                Existed: false);
        }

        try
        {
            await using var connected = await _clientFactory.ConnectAsync(job.ConnectionProfileId, cancellationToken);
            var existed = DeleteRemotePath(connected.Client, item.RemotePath, item.IsGroup, cancellationToken);
            return new DeleteQueueItemFileResult(
                DeleteQueueItemFileStatus.Deleted,
                item.Id,
                "remote",
                item.RemotePath,
                existed);
        }
        catch (Exception ex) when (ex is SshException or IOException or InvalidOperationException)
        {
            return new DeleteQueueItemFileResult(
                DeleteQueueItemFileStatus.Failed,
                item.Id,
                "remote",
                item.RemotePath,
                Existed: true,
                ex.Message);
        }
    }

    private static DeleteQueueItemFileResult NotFound(Guid queueItemId, string target)
    {
        return new DeleteQueueItemFileResult(
            DeleteQueueItemFileStatus.NotFound,
            queueItemId,
            target,
            string.Empty,
            Existed: false);
    }

    private static bool DeleteLocalPath(string path, bool isGroup)
    {
        if (isGroup && Directory.Exists(path))
        {
            Directory.Delete(path, recursive: true);
            return true;
        }

        if (File.Exists(path))
        {
            File.Delete(path);
            return true;
        }

        return false;
    }

    private static bool DeleteRemotePath(
        Renci.SshNet.SftpClient client,
        string path,
        bool isGroup,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (string.IsNullOrWhiteSpace(path) || path.TrimEnd('/') == string.Empty)
        {
            throw new InvalidOperationException("Refusing to delete an empty or root remote path.");
        }

        try
        {
            if (isGroup)
            {
                DeleteRemoteDirectory(client, path, cancellationToken);
            }
            else
            {
                client.DeleteFile(path);
            }

            return true;
        }
        catch (SftpPathNotFoundException)
        {
            return false;
        }
    }

    private static void DeleteRemoteDirectory(
        Renci.SshNet.SftpClient client,
        string path,
        CancellationToken cancellationToken)
    {
        foreach (var entry in client.ListDirectory(path))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (entry.Name is "." or "..")
            {
                continue;
            }

            if (entry.IsDirectory)
            {
                DeleteRemoteDirectory(client, entry.FullName, cancellationToken);
            }
            else
            {
                client.DeleteFile(entry.FullName);
            }
        }

        client.DeleteDirectory(path.TrimEnd('/'));
    }
}
