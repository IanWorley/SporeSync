using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
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
        var job = CreateJob(Guid.NewGuid(), Guid.NewGuid(), _destinationRoot);
        var item = CreateItem(
            job.Id,
            runId,
            "/remote/file.txt",
            Path.Combine(_destinationRoot, "file.txt"),
            "downloading",
            retryCount: 3);
        var queueRepository = new RecordingQueueRepository(item);
        var runRepository = new RecordingRunRepository(CreateRun(runId, job.Id, "downloading"));

        await using var provider = CreateProvider(
            job,
            queueRepository,
            runRepository,
            new SftpFileDownloader(
                new ThrowingSftpClientFactory(new SshHostKeyMismatchException(
                    "sftp.example.com",
                    22,
                    "SHA256:expected",
                    "SHA256:actual")),
                new LocalDestinationPathSandbox(Options.Create(new SporeSyncOptions
                {
                    DestinationRootPath = _destinationRoot
                })),
                Options.Create(new SporeSyncOptions
                {
                    DestinationRootPath = _destinationRoot
                }),
                NullLogger<SftpFileDownloader>.Instance));

        var worker = provider.GetRequiredService<DownloadWorkerHostedService>();

        var processed = await worker.ProcessNextItemAsync(CancellationToken.None);

        Assert.True(processed);
        Assert.Empty(queueRepository.Updates);
        var failed = queueRepository.Item(item.Id);
        Assert.Equal("failed", failed.Status);
        Assert.Equal(0, failed.BytesDownloaded);
        Assert.Contains("SSH host key verification failed", failed.ErrorMessage);
        Assert.NotNull(queueRepository.Item(item.Id).CompletedAt);
        Assert.Equal("completed", runRepository.LastStatusUpdate?.Status);
    }

    [Fact]
    public async Task ProcessNextItemAsync_StopsGroupAfterRunCancelled()
    {
        var runId = Guid.NewGuid();
        var job = CreateJob(Guid.NewGuid(), Guid.NewGuid(), "/local");
        var now = DateTimeOffset.UtcNow;
        var group = CreateItem(job.Id, runId, "/remote/show/", "/local/show", "queued", now, isGroup: true, childCount: 2, fileSizeBytes: 20);
        var firstLeaf = CreateItem(job.Id, runId, "/remote/show/one.txt", "/local/show/one.txt", "queued", now, fileSizeBytes: 10, groupRemotePath: group.RemotePath);
        var secondLeaf = CreateItem(job.Id, runId, "/remote/show/two.txt", "/local/show/two.txt", "queued", now, fileSizeBytes: 10, groupRemotePath: group.RemotePath);

        var runRepository = new RecordingRunRepository(CreateRun(runId, job.Id, "downloading"));
        var queueRepository = new RecordingQueueRepository(group, [firstLeaf, secondLeaf]);
        var downloader = new SuccessfulDownloader(() =>
        {
            queueRepository.CancelQueuedItems(runId);
            runRepository.CancelRun();
        });
        await using var provider = CreateProvider(job, queueRepository, runRepository, downloader);
        var worker = provider.GetRequiredService<DownloadWorkerHostedService>();

        Assert.True(await worker.ProcessNextItemAsync(CancellationToken.None));

        Assert.Equal([firstLeaf.RemotePath], downloader.DownloadedRemotePaths);
        Assert.Equal("completed", queueRepository.Item(firstLeaf.Id).Status);
        Assert.Equal("skipped", queueRepository.Item(secondLeaf.Id).Status);
        Assert.Equal("run_cancelled", queueRepository.Item(secondLeaf.Id).HandledReason);
        Assert.Equal("skipped", queueRepository.Item(group.Id).Status);
        Assert.Equal("run_cancelled", queueRepository.Item(group.Id).HandledReason);
        Assert.Equal("cancelled", runRepository.RecalculatedStatus);
        Assert.False(runRepository.CompletedRun);
    }

    [Fact]
    public async Task ProcessNextItemAsync_DoesNotDownloadLeaf_WhenRunCancelledAfterLeafRefresh()
    {
        var runId = Guid.NewGuid();
        var job = CreateJob(Guid.NewGuid(), Guid.NewGuid(), "/local");
        var now = DateTimeOffset.UtcNow;
        var group = CreateItem(job.Id, runId, "/remote/show/", "/local/show", "queued", now, isGroup: true, childCount: 2, fileSizeBytes: 20);
        var completedLeaf = CreateItem(job.Id, runId, "/remote/show/one.txt", "/local/show/one.txt", "completed", now, fileSizeBytes: 10, groupRemotePath: group.RemotePath);
        var secondLeaf = CreateItem(job.Id, runId, "/remote/show/two.txt", "/local/show/two.txt", "queued", now, fileSizeBytes: 10, groupRemotePath: group.RemotePath);

        var runRepository = new RecordingRunRepository(CreateRun(runId, job.Id, "downloading"));
        var queueRepository = new RecordingQueueRepository(group, [completedLeaf, secondLeaf]);
        var cancelledAfterRefresh = false;
        queueRepository.AfterGetById = id =>
        {
            if (id != secondLeaf.Id || cancelledAfterRefresh)
            {
                return;
            }

            cancelledAfterRefresh = true;
            queueRepository.CancelQueuedItems(runId);
            runRepository.CancelRun();
        };

        var downloader = new SuccessfulDownloader();
        await using var provider = CreateProvider(job, queueRepository, runRepository, downloader);
        var worker = provider.GetRequiredService<DownloadWorkerHostedService>();

        Assert.True(await worker.ProcessNextItemAsync(CancellationToken.None));

        Assert.Empty(downloader.DownloadedRemotePaths);
        Assert.Equal("skipped", queueRepository.Item(secondLeaf.Id).Status);
        Assert.Equal("run_cancelled", queueRepository.Item(secondLeaf.Id).HandledReason);
        Assert.Equal("skipped", queueRepository.Item(group.Id).Status);
        Assert.Equal("run_cancelled", queueRepository.Item(group.Id).HandledReason);
        Assert.Equal("cancelled", runRepository.RecalculatedStatus);
        Assert.False(runRepository.CompletedRun);
    }

    [Fact]
    public async Task ProcessNextItemAsync_DoesNotCompleteGroup_WhenRunCancelledAfterFinalLeaf()
    {
        var runId = Guid.NewGuid();
        var job = CreateJob(Guid.NewGuid(), Guid.NewGuid(), "/local");
        var now = DateTimeOffset.UtcNow;
        var group = CreateItem(job.Id, runId, "/remote/show/", "/local/show", "queued", now, isGroup: true, childCount: 1, fileSizeBytes: 10);
        var leaf = CreateItem(job.Id, runId, "/remote/show/one.txt", "/local/show/one.txt", "queued", now, fileSizeBytes: 10, groupRemotePath: group.RemotePath);

        var runRepository = new RecordingRunRepository(CreateRun(runId, job.Id, "downloading"));
        var queueRepository = new RecordingQueueRepository(group, [leaf]);
        var downloader = new SuccessfulDownloader(runRepository.CancelRun);
        await using var provider = CreateProvider(job, queueRepository, runRepository, downloader);
        var worker = provider.GetRequiredService<DownloadWorkerHostedService>();

        Assert.True(await worker.ProcessNextItemAsync(CancellationToken.None));

        Assert.Equal("completed", queueRepository.Item(leaf.Id).Status);
        Assert.Equal("skipped", queueRepository.Item(group.Id).Status);
        Assert.Equal("run_cancelled", queueRepository.Item(group.Id).HandledReason);
        Assert.Equal("cancelled", runRepository.RecalculatedStatus);
        Assert.False(runRepository.CompletedRun);
    }

    private ServiceProvider CreateProvider(
        SporeSyncJob job,
        IDownloadQueueItemRepository queueRepository,
        ISporeSyncRunRepository runRepository,
        ISftpFileDownloader downloader)
    {
        var services = new ServiceCollection();
        services.AddSingleton(Options.Create(new SporeSyncOptions
        {
            DestinationRootPath = _destinationRoot,
            DownloadPollIntervalMs = 1
        }));
        services.AddSingleton(new SporeSyncMetrics());
        services.AddSingleton(new DownloadRetryPolicy(Options.Create(new SporeSyncOptions())));
        services.AddSingleton(queueRepository);
        services.AddSingleton(runRepository);
        services.AddSingleton<ISporeSyncJobRepository>(new SingleJobRepository(job));
        services.AddSingleton(downloader);
        services.AddSingleton<ISftpClientFactory, SuccessfulSftpClientFactory>();
        services.AddSingleton<ISyncDashboardNotifier, NoOpNotifier>();
        services.AddSingleton<DownloadWorkerHostedService>();
        services.AddSingleton<ILogger<DownloadWorkerHostedService>>(NullLogger<DownloadWorkerHostedService>.Instance);
        return services.BuildServiceProvider();
    }

    private static SporeSyncJob CreateJob(Guid jobId, Guid profileId, string destinationPath)
    {
        return new SporeSyncJob
        {
            Id = jobId,
            ConnectionProfileId = profileId,
            Name = "job",
            SourcePath = "/remote",
            DestinationPath = destinationPath,
            PollingIntervalSeconds = 60,
            IsEnabled = true
        };
    }

    private static DownloadQueueItem CreateItem(
        Guid jobId,
        Guid runId,
        string remotePath,
        string destinationPath,
        string status,
        DateTimeOffset? now = null,
        bool isGroup = false,
        int childCount = 0,
        long fileSizeBytes = 10,
        string? groupRemotePath = null,
        int retryCount = 0)
    {
        var timestamp = now ?? DateTimeOffset.UtcNow;
        return new DownloadQueueItem
        {
            Id = Guid.NewGuid(),
            JobId = jobId,
            SyncRunId = runId,
            RemotePath = remotePath,
            DestinationPath = destinationPath,
            FileSizeBytes = fileSizeBytes,
            RemoteModifiedAt = timestamp,
            Status = status,
            BytesDownloaded = 0,
            RetryCount = retryCount,
            QueuedAt = timestamp,
            StartedAt = status == "downloading" ? timestamp : null,
            UpdatedAt = timestamp,
            IsGroup = isGroup,
            GroupRemotePath = groupRemotePath,
            ChildCount = childCount
        };
    }

    private static DownloadQueueItem CopyWith(
        DownloadQueueItem item,
        string? status = null,
        long? bytesDownloaded = null,
        decimal? currentBytesPerSecond = null,
        string? errorMessage = null,
        string? handledReason = null,
        DateTimeOffset? completedAt = null,
        int? retryCount = null)
    {
        return new DownloadQueueItem
        {
            Id = item.Id,
            JobId = item.JobId,
            SyncRunId = item.SyncRunId,
            RemotePath = item.RemotePath,
            DestinationPath = item.DestinationPath,
            FileSizeBytes = item.FileSizeBytes,
            RemoteModifiedAt = item.RemoteModifiedAt,
            Status = status ?? item.Status,
            BytesDownloaded = bytesDownloaded ?? item.BytesDownloaded,
            CurrentBytesPerSecond = currentBytesPerSecond,
            RetryCount = retryCount ?? item.RetryCount,
            HandledReason = handledReason,
            ErrorMessage = errorMessage,
            QueuedAt = item.QueuedAt,
            StartedAt = item.StartedAt,
            CompletedAt = completedAt ?? item.CompletedAt,
            UpdatedAt = DateTimeOffset.UtcNow,
            IsGroup = item.IsGroup,
            GroupRemotePath = item.GroupRemotePath,
            ChildCount = item.ChildCount
        };
    }

    private static SporeSyncRun CreateRun(Guid runId, Guid jobId, string status)
    {
        return new SporeSyncRun
        {
            Id = runId,
            JobId = jobId,
            JobName = "job",
            Status = status,
            StartedAt = DateTimeOffset.UtcNow,
            CompletedAt = status is "completed" or "cancelled" ? DateTimeOffset.UtcNow : null,
            TotalFileCount = 1,
            CompletedFileCount = 0,
            SkippedFileCount = 0,
            FailedFileCount = 0,
            TotalBytes = 20,
            DownloadedBytes = 0
        };
    }

    private static SporeSyncRun CopyRunWith(SporeSyncRun run, string status)
    {
        return new SporeSyncRun
        {
            Id = run.Id,
            JobId = run.JobId,
            JobName = run.JobName,
            Status = status,
            StartedAt = run.StartedAt,
            CompletedAt = status is "completed" or "cancelled" ? DateTimeOffset.UtcNow : run.CompletedAt,
            TotalFileCount = run.TotalFileCount,
            CompletedFileCount = run.CompletedFileCount,
            SkippedFileCount = run.SkippedFileCount,
            FailedFileCount = run.FailedFileCount,
            TotalBytes = run.TotalBytes,
            DownloadedBytes = run.DownloadedBytes,
            CurrentBytesPerSecond = run.CurrentBytesPerSecond,
            ErrorMessage = run.ErrorMessage
        };
    }

    private sealed class RecordingQueueRepository : IDownloadQueueItemRepository
    {
        private readonly Dictionary<Guid, DownloadQueueItem> _items;
        private readonly Guid _claimId;
        private readonly List<Guid> _leafIds;
        private bool _claimed;

        public RecordingQueueRepository(DownloadQueueItem item)
        {
            _claimId = item.Id;
            _leafIds = [];
            _items = new Dictionary<Guid, DownloadQueueItem> { [item.Id] = item };
        }

        public RecordingQueueRepository(DownloadQueueItem group, IReadOnlyList<DownloadQueueItem> leaves)
        {
            _claimId = group.Id;
            _leafIds = leaves.Select(leaf => leaf.Id).ToList();
            _items = leaves.Concat([group]).ToDictionary(item => item.Id);
        }

        public List<UpdateDownloadQueueItemProgress> Updates { get; } = [];

        public Action<Guid>? AfterGetById { get; set; }

        public DownloadQueueItem Item(Guid id) => _items[id];

        public void CancelQueuedItems(Guid runId)
        {
            foreach (var item in _items.Values.Where(item => item.SyncRunId == runId && item.Status == "queued").ToArray())
            {
                _items[item.Id] = CopyWith(
                    item,
                    status: "skipped",
                    handledReason: "run_cancelled",
                    completedAt: DateTimeOffset.UtcNow);
            }
        }

        public Task<DownloadQueueItem?> ClaimNextAsync(int leaseSeconds, CancellationToken cancellationToken = default)
        {
            if (_claimed)
            {
                return Task.FromResult<DownloadQueueItem?>(null);
            }

            _claimed = true;
            var claimed = CopyWith(_items[_claimId], status: "downloading");
            _items[_claimId] = claimed;
            return Task.FromResult<DownloadQueueItem?>(claimed);
        }

        public Task<DownloadQueueItem?> ClaimGroupLeafAsync(
            Guid id,
            Guid runId,
            string groupRemotePath,
            int leaseSeconds,
            CancellationToken cancellationToken = default)
        {
            if (!_items.TryGetValue(id, out var item)
                || item.SyncRunId != runId
                || item.GroupRemotePath != groupRemotePath
                || !string.Equals(item.Status, "queued", StringComparison.OrdinalIgnoreCase))
            {
                return Task.FromResult<DownloadQueueItem?>(null);
            }

            var claimed = CopyWith(item, status: "downloading");
            _items[id] = claimed;
            return Task.FromResult<DownloadQueueItem?>(claimed);
        }

        public Task<bool> RenewLeaseAsync(Guid id, int leaseSeconds, CancellationToken cancellationToken = default)
            => Task.FromResult(_items.ContainsKey(id));

        public Task<DownloadQueueItem?> ReleaseAsync(Guid id, CancellationToken cancellationToken = default)
            => Task.FromResult(_items.GetValueOrDefault(id));

        public Task<IReadOnlyList<DownloadQueueItem>> RequeueStaleAsync(
            bool ignoreLeases,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<IReadOnlyList<DownloadQueueItem>> GetLeavesForGroupAsync(
            Guid runId,
            string groupRemotePath,
            CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<DownloadQueueItem>>(_leafIds.Select(id => _items[id]).ToList());

        public Task<DownloadQueueItem?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
        {
            var item = _items.GetValueOrDefault(id);
            AfterGetById?.Invoke(id);
            return Task.FromResult(item);
        }

        public Task<DownloadQueueItem> UpdateProgressAsync(
            UpdateDownloadQueueItemProgress update,
            CancellationToken cancellationToken = default)
        {
            Updates.Add(update);
            var current = _items[update.Id];
            var updated = CopyWith(
                current,
                update.Status,
                update.BytesDownloaded,
                update.CurrentBytesPerSecond,
                update.ErrorMessage,
                update.HandledReason,
                update.Status is "completed" or "failed" or "skipped" ? DateTimeOffset.UtcNow : null);
            _items[update.Id] = updated;
            return Task.FromResult(updated);
        }

        public Task<DownloadQueueItem> RecordFailureAsync(
            Guid id,
            string? errorMessage,
            int maxRetries,
            DateTimeOffset nextAttemptAt,
            long? bytesDownloaded = null,
            CancellationToken cancellationToken = default)
        {
            var current = _items[id];
            var retryCount = current.RetryCount + 1;
            var failed = retryCount > maxRetries;
            var updated = CopyWith(
                current,
                failed ? "failed" : "queued",
                bytesDownloaded ?? current.BytesDownloaded,
                errorMessage: errorMessage,
                handledReason: failed ? "retry_budget_exhausted" : "retry_scheduled",
                completedAt: failed ? DateTimeOffset.UtcNow : null,
                retryCount: retryCount);
            _items[id] = updated;
            return Task.FromResult(updated);
        }

        public Task<DownloadQueueItem> DeferAsync(
            Guid id,
            DateTimeOffset nextAttemptAt,
            string reason,
            long? bytesDownloaded = null,
            CancellationToken cancellationToken = default)
        {
            var current = _items[id];
            var updated = CopyWith(current, "queued", bytesDownloaded ?? current.BytesDownloaded, handledReason: reason);
            _items[id] = updated;
            return Task.FromResult(updated);
        }

        public Task<PagedResult<DownloadQueueItem>> GetByRunIdAsync(
            Guid runId,
            QueueItemQuery query,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<DownloadQueueItem> UpsertAsync(UpsertDownloadQueueItem item, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<IReadOnlyList<DownloadQueueItem>> UpsertManyAsync(
            IReadOnlyCollection<UpsertDownloadQueueItem> items,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<IReadOnlyDictionary<string, SyncedRemoteState>> GetSyncedStateAsync(Guid jobId, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<IReadOnlyList<DownloadQueueItem>> MarkRemoteDeletedAsync(
            Guid jobId,
            Guid syncRunId,
            IReadOnlyCollection<string> remotePaths,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<int> RequeueFailedAsync(Guid jobId, Guid syncRunId, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<DownloadQueueItem?> RetryAsync(Guid id, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
    }

    private sealed class RecordingRunRepository : ISporeSyncRunRepository
    {
        private SporeSyncRun _run;

        public RecordingRunRepository(SporeSyncRun run)
        {
            _run = run;
        }

        public string? RecalculatedStatus { get; private set; }

        public bool CompletedRun { get; private set; }

        public UpdateSporeSyncRunStatus? LastStatusUpdate { get; private set; }

        public void CancelRun()
        {
            _run = CopyRunWith(_run, "cancelled");
        }

        public Task<SporeSyncRun?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
            => Task.FromResult<SporeSyncRun?>(_run.Id == id ? _run : null);

        public Task<SporeSyncRun> RecalculateAggregatesAsync(Guid runId, CancellationToken cancellationToken = default)
        {
            RecalculatedStatus = _run.Status;
            return Task.FromResult(_run);
        }

        public Task<bool> HasPendingDownloadsAsync(Guid runId, CancellationToken cancellationToken = default)
            => Task.FromResult(false);

        public Task<SporeSyncRun> UpdateStatusAsync(UpdateSporeSyncRunStatus update, CancellationToken cancellationToken = default)
        {
            LastStatusUpdate = update;
            CompletedRun = string.Equals(update.Status, "completed", StringComparison.OrdinalIgnoreCase);
            _run = CopyRunWith(_run, update.Status);
            return Task.FromResult(_run);
        }

        public Task<PagedResult<SporeSyncRun>> GetRunsAsync(RunQuery query, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<SporeSyncRun?> TryCreateAsync(
            Guid jobId,
            int leaseSeconds = 1800,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<bool> HasActiveRunAsync(Guid jobId, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<bool> RenewLeaseAsync(Guid runId, int leaseSeconds, CancellationToken cancellationToken = default)
            => Task.FromResult(_run.Id == runId);

        public Task<SyncHistoryPruneResult> PruneHistoryAsync(DateTimeOffset cutoff, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<IReadOnlyList<SporeSyncRun>> ReapOrphanedAsync(
            bool ignoreLeases,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<int> RetryFailedItemsAsync(Guid runId, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<SporeSyncRun> AdvanceScanStatusAsync(
            UpdateSporeSyncRunStatus update,
            string expectedStatus,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<SporeSyncRun?> CancelAsync(Guid runId, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
    }

    private sealed class SingleJobRepository : ISporeSyncJobRepository
    {
        private readonly SporeSyncJob _job;

        public SingleJobRepository(SporeSyncJob job)
        {
            _job = job;
        }

        public Task<SporeSyncJob?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
            => Task.FromResult<SporeSyncJob?>(_job.Id == id ? _job : null);

        public Task<IReadOnlyCollection<SporeSyncJob>> GetAllAsync(CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<SporeSyncJob> UpsertAsync(UpsertSporeSyncJob job, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<IReadOnlyCollection<SporeSyncJob>> GetDueJobsAsync(CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task MarkPolledAsync(Guid id, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<SafeDeleteSporeSyncJobResult> SafeDeleteAsync(
            Guid id,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<int> CountByConnectionProfileAsync(Guid connectionProfileId, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
    }

    private sealed class SuccessfulDownloader : ISftpFileDownloader
    {
        private readonly Action? _afterDownload;

        public SuccessfulDownloader(Action? afterDownload = null)
        {
            _afterDownload = afterDownload;
        }

        public List<string> DownloadedRemotePaths { get; } = [];

        public Task<SftpDownloadResult> DownloadAsync(
            Guid connectionProfileId,
            string remotePath,
            string localPath,
            CancellationToken cancellationToken = default)
            => DownloadAsync(connectionProfileId, remotePath, localPath, null, cancellationToken);

        public Task<SftpDownloadResult> DownloadAsync(
            Guid connectionProfileId,
            string remotePath,
            string localPath,
            IProgress<long>? progress,
            CancellationToken cancellationToken = default)
        {
            DownloadedRemotePaths.Add(remotePath);
            progress?.Report(10);
            _afterDownload?.Invoke();
            return Task.FromResult(new SftpDownloadResult(true, 10, 100, null));
        }

        public Task<SftpDownloadResult> DownloadAsync(
            IConnectedSftpClient client,
            string remotePath,
            string localPath,
            IProgress<long>? progress = null,
            CancellationToken cancellationToken = default)
        {
            DownloadedRemotePaths.Add(remotePath);
            progress?.Report(10);
            _afterDownload?.Invoke();
            return Task.FromResult(new SftpDownloadResult(true, 10, 100, null));
        }
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

    private sealed class SuccessfulSftpClientFactory : ISftpClientFactory
    {
        public Task<IConnectedSftpClient> ConnectAsync(
            Guid connectionProfileId,
            CancellationToken cancellationToken = default)
            => Task.FromResult<IConnectedSftpClient>(new FakeConnectedSftpClient());
    }

    private sealed class FakeConnectedSftpClient : IConnectedSftpClient
    {
        public Renci.SshNet.SftpClient Client => throw new NotSupportedException();

        public bool IsConnected { get; private set; } = true;

        public ValueTask DisposeAsync()
        {
            IsConnected = false;
            return ValueTask.CompletedTask;
        }
    }

    private sealed class NoOpNotifier : ISyncDashboardNotifier
    {
        public Task NotifyRunUpdatedAsync(SporeSyncRun run, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task NotifyQueueItemUpdatedAsync(DownloadQueueItem item, CancellationToken cancellationToken = default)
            => Task.CompletedTask;
    }
}
