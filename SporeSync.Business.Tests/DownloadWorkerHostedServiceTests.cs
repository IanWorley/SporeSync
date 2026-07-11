using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SporeSync.Business.Interface;
using SporeSync.Business.Observability;
using SporeSync.Business.Security;
using SporeSync.Business.Sftp;
using SporeSync.Business.Worker;
using SporeSync.Domain.Interface;
using SporeSync.Domain.Model;

namespace SporeSync.Business.Tests;

public sealed class DownloadWorkerHostedServiceTests : IDisposable
{
    private readonly string _destinationRoot = Directory.CreateTempSubdirectory("sporesync-worker-").FullName;

    public void Dispose()
    {
        if (Directory.Exists(_destinationRoot))
        {
            Directory.Delete(_destinationRoot, recursive: true);
        }
    }

    [Fact]
    public async Task ProcessNextItemAsync_MarksClaimedItemFailed_WhenHostKeyMismatchFailsConnect()
    {
        var runId = Guid.NewGuid();
        var job = new SporeSyncJob
        {
            Id = Guid.NewGuid(),
            ConnectionProfileId = Guid.NewGuid(),
            Name = "host-key-mismatch",
            SourcePath = "/remote",
            DestinationPath = _destinationRoot,
            PollingIntervalSeconds = 120,
            IsEnabled = true
        };
        var item = CreateQueueItem(job.Id, runId, Path.Combine(_destinationRoot, "file.txt"));
        var queueRepository = new RecordingDownloadQueueItemRepository(item);
        var runRepository = new RecordingSporeSyncRunRepository(job.Id, runId);

        await using var provider = CreateProvider(
            job,
            queueRepository,
            runRepository,
            new SshHostKeyMismatchException(
                "sftp.example.com",
                22,
                "SHA256:expected",
                "SHA256:actual"));

        var worker = provider.GetRequiredService<DownloadWorkerHostedService>();

        var processed = await worker.ProcessNextItemAsync(CancellationToken.None);

        Assert.True(processed);
        var failed = Assert.Single(queueRepository.Updates);
        Assert.Equal(item.Id, failed.Id);
        Assert.Equal("failed", failed.Status);
        Assert.Equal(0, failed.BytesDownloaded);
        Assert.Contains("SSH host key verification failed", failed.ErrorMessage);
        Assert.NotNull(queueRepository.CurrentItem.CompletedAt);
        Assert.Equal("completed", runRepository.LastStatusUpdate?.Status);
    }

    private ServiceProvider CreateProvider(
        SporeSyncJob job,
        IDownloadQueueItemRepository queueRepository,
        ISporeSyncRunRepository runRepository,
        Exception connectException)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(Options.Create(new SporeSyncOptions
        {
            DestinationRootPath = _destinationRoot,
            DownloadPollIntervalMs = 1
        }));
        services.AddSingleton<SporeSyncMetrics>();
        services.AddSingleton<ISyncDashboardNotifier, RecordingSyncDashboardNotifier>();
        services.AddSingleton<IDownloadQueueItemRepository>(queueRepository);
        services.AddSingleton<ISporeSyncRunRepository>(runRepository);
        services.AddSingleton<ISporeSyncJobRepository>(new SingleJobRepository(job));
        services.AddSingleton<ISftpClientFactory>(new ThrowingSftpClientFactory(connectException));
        services.AddSingleton<LocalDestinationPathSandbox>();
        services.AddSingleton<SftpFileDownloader>();
        services.AddSingleton<DownloadWorkerHostedService>();
        return services.BuildServiceProvider();
    }

    private static DownloadQueueItem CreateQueueItem(Guid jobId, Guid runId, string destinationPath)
    {
        var now = DateTimeOffset.UtcNow;
        return new DownloadQueueItem
        {
            Id = Guid.NewGuid(),
            JobId = jobId,
            SyncRunId = runId,
            RemotePath = "/remote/file.txt",
            DestinationPath = destinationPath,
            FileSizeBytes = 100,
            RemoteModifiedAt = now,
            Status = "downloading",
            BytesDownloaded = 0,
            RetryCount = 0,
            QueuedAt = now,
            StartedAt = now,
            UpdatedAt = now,
            IsGroup = false,
            ChildCount = 0
        };
    }

    private static DownloadQueueItem ApplyUpdate(DownloadQueueItem item, UpdateDownloadQueueItemProgress update)
    {
        var now = DateTimeOffset.UtcNow;
        return new DownloadQueueItem
        {
            Id = item.Id,
            JobId = item.JobId,
            SyncRunId = item.SyncRunId,
            RemotePath = item.RemotePath,
            DestinationPath = item.DestinationPath,
            FileSizeBytes = item.FileSizeBytes,
            RemoteModifiedAt = item.RemoteModifiedAt,
            Status = update.Status,
            BytesDownloaded = update.BytesDownloaded,
            CurrentBytesPerSecond = update.CurrentBytesPerSecond,
            RetryCount = item.RetryCount,
            HandledReason = update.HandledReason,
            ErrorMessage = update.ErrorMessage,
            QueuedAt = item.QueuedAt,
            StartedAt = item.StartedAt,
            CompletedAt = update.Status is "completed" or "failed" or "skipped" ? now : null,
            UpdatedAt = now,
            IsGroup = item.IsGroup,
            GroupRemotePath = item.GroupRemotePath,
            ChildCount = item.ChildCount
        };
    }

    private sealed class ThrowingSftpClientFactory : ISftpClientFactory
    {
        private readonly Exception _exception;

        public ThrowingSftpClientFactory(Exception exception)
        {
            _exception = exception;
        }

        public Task<IConnectedSftpClient> ConnectAsync(
            Guid connectionProfileId,
            CancellationToken cancellationToken = default)
        {
            throw _exception;
        }
    }

    private sealed class RecordingDownloadQueueItemRepository : IDownloadQueueItemRepository
    {
        private bool _claimed;

        public RecordingDownloadQueueItemRepository(DownloadQueueItem item)
        {
            CurrentItem = item;
        }

        public DownloadQueueItem CurrentItem { get; private set; }

        public List<UpdateDownloadQueueItemProgress> Updates { get; } = [];

        public Task<PagedResult<DownloadQueueItem>> GetByRunIdAsync(
            Guid runId,
            QueueItemQuery query,
            CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public Task<DownloadQueueItem?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
            => Task.FromResult<DownloadQueueItem?>(CurrentItem.Id == id ? CurrentItem : null);

        public Task<IReadOnlyList<DownloadQueueItem>> GetLeavesForGroupAsync(
            Guid runId,
            string groupRemotePath,
            CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public Task<DownloadQueueItem> UpsertAsync(
            UpsertDownloadQueueItem item,
            CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public Task<IReadOnlyDictionary<string, SyncedRemoteState>> GetSyncedStateAsync(
            Guid jobId,
            CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public Task<DownloadQueueItem?> ClaimNextAsync(CancellationToken cancellationToken = default)
        {
            if (_claimed)
            {
                return Task.FromResult<DownloadQueueItem?>(null);
            }

            _claimed = true;
            return Task.FromResult<DownloadQueueItem?>(CurrentItem);
        }

        public Task<DownloadQueueItem> UpdateProgressAsync(
            UpdateDownloadQueueItemProgress update,
            CancellationToken cancellationToken = default)
        {
            Updates.Add(update);
            CurrentItem = ApplyUpdate(CurrentItem, update);
            return Task.FromResult(CurrentItem);
        }

        public Task<IReadOnlyList<DownloadQueueItem>> MarkRemoteDeletedAsync(
            Guid jobId,
            Guid syncRunId,
            IReadOnlyCollection<string> remotePaths,
            CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public Task<int> RequeueFailedAsync(Guid jobId, Guid syncRunId, CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }
    }

    private sealed class SingleJobRepository : ISporeSyncJobRepository
    {
        private readonly SporeSyncJob _job;

        public SingleJobRepository(SporeSyncJob job)
        {
            _job = job;
        }

        public Task<IReadOnlyCollection<SporeSyncJob>> GetAllAsync(CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public Task<SporeSyncJob?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
            => Task.FromResult<SporeSyncJob?>(_job.Id == id ? _job : null);

        public Task<SporeSyncJob> UpsertAsync(UpsertSporeSyncJob job, CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public Task<IReadOnlyCollection<SporeSyncJob>> GetDueJobsAsync(CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public Task MarkPolledAsync(Guid id, CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }
    }

    private sealed class RecordingSporeSyncRunRepository : ISporeSyncRunRepository
    {
        private readonly Guid _jobId;
        private readonly Guid _runId;

        public RecordingSporeSyncRunRepository(Guid jobId, Guid runId)
        {
            _jobId = jobId;
            _runId = runId;
        }

        public UpdateSporeSyncRunStatus? LastStatusUpdate { get; private set; }

        public Task<PagedResult<SporeSyncRun>> GetRunsAsync(
            RunQuery query,
            CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public Task<SporeSyncRun?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public Task<SporeSyncRun> CreateAsync(Guid jobId, CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public Task<SporeSyncRun> UpdateStatusAsync(
            UpdateSporeSyncRunStatus update,
            CancellationToken cancellationToken = default)
        {
            LastStatusUpdate = update;
            return Task.FromResult(CreateRun(update.Status));
        }

        public Task<bool> HasActiveRunAsync(Guid jobId, CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public Task<SporeSyncRun> RecalculateAggregatesAsync(
            Guid runId,
            CancellationToken cancellationToken = default)
            => Task.FromResult(CreateRun("downloading"));

        public Task<bool> HasPendingDownloadsAsync(Guid runId, CancellationToken cancellationToken = default)
            => Task.FromResult(false);

        public Task<SyncHistoryPruneResult> PruneHistoryAsync(
            DateTimeOffset cutoff,
            CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        private SporeSyncRun CreateRun(string status)
        {
            return new SporeSyncRun
            {
                Id = _runId,
                JobId = _jobId,
                JobName = "host-key-mismatch",
                Status = status,
                StartedAt = DateTimeOffset.UtcNow,
                CompletedAt = status == "completed" ? DateTimeOffset.UtcNow : null,
                TotalFileCount = 1,
                CompletedFileCount = 0,
                SkippedFileCount = 0,
                FailedFileCount = 1,
                TotalBytes = 100,
                DownloadedBytes = 0
            };
        }
    }

    private sealed class RecordingSyncDashboardNotifier : ISyncDashboardNotifier
    {
        public Task NotifyRunUpdatedAsync(SporeSyncRun run, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task NotifyQueueItemUpdatedAsync(DownloadQueueItem item, CancellationToken cancellationToken = default)
            => Task.CompletedTask;
    }
}
