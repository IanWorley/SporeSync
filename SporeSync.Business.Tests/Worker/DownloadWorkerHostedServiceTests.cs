using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SporeSync.Business;
using SporeSync.Business.Interface;
using SporeSync.Business.Observability;
using SporeSync.Business.Sftp;
using SporeSync.Business.Worker;
using SporeSync.Domain.Interface;
using SporeSync.Domain.Model;

namespace SporeSync.Business.Tests.Worker;

public sealed class DownloadWorkerHostedServiceTests
{
    private static readonly Guid JobId = Guid.NewGuid();
    private static readonly Guid RunId = Guid.NewGuid();

    [Fact]
    public async Task ProcessNextItem_SuccessfulDownload_MarksItemCompleted()
    {
        var item = CreateItem("/remote/file.txt");
        var queueRepository = new FakeQueueRepository(item);
        var downloader = new FakeDownloader(_ => SftpDownloadResult.Succeed(100, 50));
        var worker = CreateWorker(queueRepository, downloader);

        var processed = await worker.ProcessNextItemAsync(CancellationToken.None);

        Assert.True(processed);
        var update = Assert.Single(queueRepository.ProgressUpdates);
        Assert.Equal("completed", update.Status);
        Assert.Equal(100, update.BytesDownloaded);
        Assert.Empty(queueRepository.FailureCalls);
        Assert.Empty(queueRepository.DeferCalls);
    }

    [Fact]
    public async Task ProcessNextItem_FailedDownload_RecordsFailureWithBackoff()
    {
        var item = CreateItem("/remote/file.txt", retryCount: 2);
        var queueRepository = new FakeQueueRepository(item);
        var downloader = new FakeDownloader(_ => SftpDownloadResult.Failure("connection reset"));
        var before = DateTimeOffset.UtcNow;
        var worker = CreateWorker(queueRepository, downloader, maxRetries: 5, baseDelaySeconds: 10);

        await worker.ProcessNextItemAsync(CancellationToken.None);

        var failure = Assert.Single(queueRepository.FailureCalls);
        Assert.Equal(item.Id, failure.Id);
        Assert.Equal("connection reset", failure.ErrorMessage);
        Assert.Equal(5, failure.MaxRetries);
        // Backoff for the third attempt (retryCount = 2): base 10s * 2^2 = 40s.
        var expectedDelay = TimeSpan.FromSeconds(40);
        Assert.InRange(
            failure.NextAttemptAt,
            before + expectedDelay,
            DateTimeOffset.UtcNow + expectedDelay);
        Assert.Empty(queueRepository.ProgressUpdates);
    }

    [Fact]
    public async Task ProcessNextItem_DeferredDownload_DefersWithoutConsumingRetryBudget()
    {
        var item = CreateItem("/remote/file.txt");
        var queueRepository = new FakeQueueRepository(item);
        var downloader = new FakeDownloader(_ => SftpDownloadResult.Defer("still uploading"));
        var worker = CreateWorker(queueRepository, downloader, stabilityWindowSeconds: 20);

        await worker.ProcessNextItemAsync(CancellationToken.None);

        var defer = Assert.Single(queueRepository.DeferCalls);
        Assert.Equal(item.Id, defer.Id);
        Assert.Equal(DownloadWorkerHostedService.AwaitingRemoteStabilityReason, defer.Reason);
        Assert.Empty(queueRepository.FailureCalls);
        Assert.Empty(queueRepository.ProgressUpdates);
    }

    [Fact]
    public async Task ProcessNextItem_GroupWithFailedLeaf_MarksLeafFailedAndRecordsGroupFailure()
    {
        var group = CreateItem("/remote/reports/", isGroup: true, childCount: 2);
        var completedLeaf = CreateItem("/remote/reports/done.txt", groupRemotePath: "/remote/reports/", status: "completed", bytesDownloaded: 100);
        var failingLeaf = CreateItem("/remote/reports/bad.txt", groupRemotePath: "/remote/reports/");
        var queueRepository = new FakeQueueRepository(group)
        {
            Leaves = [completedLeaf, failingLeaf]
        };
        var downloader = new FakeDownloader(remotePath =>
            remotePath == failingLeaf.RemotePath
                ? SftpDownloadResult.Failure("boom")
                : SftpDownloadResult.Succeed(100, null));
        var worker = CreateWorker(queueRepository, downloader);

        await worker.ProcessNextItemAsync(CancellationToken.None);

        var leafUpdate = Assert.Single(queueRepository.ProgressUpdates);
        Assert.Equal(failingLeaf.Id, leafUpdate.Id);
        Assert.Equal("failed", leafUpdate.Status);

        var failure = Assert.Single(queueRepository.FailureCalls);
        Assert.Equal(group.Id, failure.Id);
        // Completed-leaf bytes are preserved on the group row while it awaits retry.
        Assert.Equal(100, failure.BytesDownloaded);
    }

    [Fact]
    public async Task ProcessNextItem_GroupWithDeferredLeaf_DefersGroup()
    {
        var group = CreateItem("/remote/reports/", isGroup: true, childCount: 1);
        var leaf = CreateItem("/remote/reports/uploading.txt", groupRemotePath: "/remote/reports/");
        var queueRepository = new FakeQueueRepository(group)
        {
            Leaves = [leaf]
        };
        var downloader = new FakeDownloader(_ => SftpDownloadResult.Defer("still uploading"));
        var worker = CreateWorker(queueRepository, downloader);

        await worker.ProcessNextItemAsync(CancellationToken.None);

        var leafUpdate = Assert.Single(queueRepository.ProgressUpdates);
        Assert.Equal(leaf.Id, leafUpdate.Id);
        Assert.Equal("queued", leafUpdate.Status);
        Assert.Equal(DownloadWorkerHostedService.AwaitingRemoteStabilityReason, leafUpdate.HandledReason);

        var defer = Assert.Single(queueRepository.DeferCalls);
        Assert.Equal(group.Id, defer.Id);
        Assert.Empty(queueRepository.FailureCalls);
    }

    [Fact]
    public async Task ProcessNextItem_GroupRetry_SkipsCompletedLeaves()
    {
        var group = CreateItem("/remote/reports/", isGroup: true, childCount: 2, retryCount: 1);
        var completedLeaf = CreateItem("/remote/reports/done.txt", groupRemotePath: "/remote/reports/", status: "completed", bytesDownloaded: 100);
        var failedLeaf = CreateItem("/remote/reports/bad.txt", groupRemotePath: "/remote/reports/", status: "failed");
        var queueRepository = new FakeQueueRepository(group)
        {
            Leaves = [completedLeaf, failedLeaf]
        };
        var downloader = new FakeDownloader(_ => SftpDownloadResult.Succeed(200, null));
        var worker = CreateWorker(queueRepository, downloader);

        await worker.ProcessNextItemAsync(CancellationToken.None);

        // Only the previously failed leaf is re-downloaded.
        Assert.Equal([failedLeaf.RemotePath], downloader.RequestedPaths);

        Assert.Equal(2, queueRepository.ProgressUpdates.Count);
        var groupUpdate = queueRepository.ProgressUpdates.Single(update => update.Id == group.Id);
        Assert.Equal("completed", groupUpdate.Status);
        Assert.Equal(300, groupUpdate.BytesDownloaded);
    }

    private static DownloadWorkerHostedService CreateWorker(
        FakeQueueRepository queueRepository,
        FakeDownloader downloader,
        int maxRetries = 3,
        int baseDelaySeconds = 30,
        int stabilityWindowSeconds = 15)
    {
        var options = Options.Create(new SporeSyncOptions
        {
            DownloadMaxRetries = maxRetries,
            DownloadRetryBaseDelaySeconds = baseDelaySeconds,
            RemoteFileStabilityWindowSeconds = stabilityWindowSeconds
        });

        var services = new ServiceCollection();
        services.AddSingleton<IDownloadQueueItemRepository>(queueRepository);
        services.AddSingleton<ISporeSyncRunRepository>(new FakeRunRepository());
        services.AddSingleton<ISporeSyncJobRepository>(new FakeJobRepository());
        services.AddSingleton<ISftpFileDownloader>(downloader);
        services.AddSingleton<ISyncDashboardNotifier>(new FakeNotifier());
        var provider = services.BuildServiceProvider();

        return new DownloadWorkerHostedService(
            provider.GetRequiredService<IServiceScopeFactory>(),
            options,
            new SporeSyncMetrics(),
            new DownloadRetryPolicy(options),
            NullLogger<DownloadWorkerHostedService>.Instance);
    }

    private static DownloadQueueItem CreateItem(
        string remotePath,
        bool isGroup = false,
        int childCount = 0,
        string? groupRemotePath = null,
        string status = "downloading",
        long bytesDownloaded = 0,
        int retryCount = 0)
    {
        return new DownloadQueueItem
        {
            Id = Guid.NewGuid(),
            JobId = JobId,
            SyncRunId = RunId,
            RemotePath = remotePath,
            DestinationPath = "/data" + remotePath.TrimEnd('/'),
            FileSizeBytes = 100,
            Status = status,
            BytesDownloaded = bytesDownloaded,
            RetryCount = retryCount,
            QueuedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
            IsGroup = isGroup,
            GroupRemotePath = groupRemotePath,
            ChildCount = childCount
        };
    }

    private sealed record FailureCall(Guid Id, string? ErrorMessage, int MaxRetries, DateTimeOffset NextAttemptAt, long? BytesDownloaded);

    private sealed record DeferCall(Guid Id, DateTimeOffset NextAttemptAt, string Reason, long? BytesDownloaded);

    private sealed class FakeQueueRepository : IDownloadQueueItemRepository
    {
        private DownloadQueueItem? _claimable;

        public FakeQueueRepository(DownloadQueueItem claimable)
        {
            _claimable = claimable;
        }

        public IReadOnlyList<DownloadQueueItem> Leaves { get; init; } = [];

        public List<UpdateDownloadQueueItemProgress> ProgressUpdates { get; } = [];

        public List<FailureCall> FailureCalls { get; } = [];

        public List<DeferCall> DeferCalls { get; } = [];

        public Task<DownloadQueueItem?> ClaimNextAsync(CancellationToken cancellationToken = default)
        {
            var item = _claimable;
            _claimable = null;
            return Task.FromResult(item);
        }

        public Task<IReadOnlyList<DownloadQueueItem>> GetLeavesForGroupAsync(
            Guid runId,
            string groupRemotePath,
            CancellationToken cancellationToken = default)
            => Task.FromResult(Leaves);

        public Task<DownloadQueueItem> UpdateProgressAsync(
            UpdateDownloadQueueItemProgress update,
            CancellationToken cancellationToken = default)
        {
            ProgressUpdates.Add(update);
            return Task.FromResult(CreateResult(update.Id, update.Status));
        }

        public Task<DownloadQueueItem> RecordFailureAsync(
            Guid id,
            string? errorMessage,
            int maxRetries,
            DateTimeOffset nextAttemptAt,
            long? bytesDownloaded = null,
            CancellationToken cancellationToken = default)
        {
            FailureCalls.Add(new FailureCall(id, errorMessage, maxRetries, nextAttemptAt, bytesDownloaded));
            return Task.FromResult(CreateResult(id, "queued"));
        }

        public Task<DownloadQueueItem> DeferAsync(
            Guid id,
            DateTimeOffset nextAttemptAt,
            string reason,
            long? bytesDownloaded = null,
            CancellationToken cancellationToken = default)
        {
            DeferCalls.Add(new DeferCall(id, nextAttemptAt, reason, bytesDownloaded));
            return Task.FromResult(CreateResult(id, "queued"));
        }

        private static DownloadQueueItem CreateResult(Guid id, string status)
        {
            return new DownloadQueueItem
            {
                Id = id,
                JobId = JobId,
                SyncRunId = RunId,
                RemotePath = "/remote/result",
                DestinationPath = "/data/result",
                FileSizeBytes = 100,
                Status = status,
                BytesDownloaded = 0,
                RetryCount = 0,
                QueuedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow,
                IsGroup = false,
                ChildCount = 0
            };
        }

        public Task<PagedResult<DownloadQueueItem>> GetByRunIdAsync(Guid runId, QueueItemQuery query, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<DownloadQueueItem?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<DownloadQueueItem> UpsertAsync(UpsertDownloadQueueItem item, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<IReadOnlyDictionary<string, SyncedRemoteState>> GetSyncedStateAsync(Guid jobId, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<IReadOnlyList<DownloadQueueItem>> MarkRemoteDeletedAsync(Guid jobId, Guid syncRunId, IReadOnlyCollection<string> remotePaths, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<int> RequeueFailedAsync(Guid jobId, Guid syncRunId, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<DownloadQueueItem?> RetryAsync(Guid id, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
    }

    private sealed class FakeDownloader : ISftpFileDownloader
    {
        private readonly Func<string, SftpDownloadResult> _resultFactory;

        public FakeDownloader(Func<string, SftpDownloadResult> resultFactory)
        {
            _resultFactory = resultFactory;
        }

        public List<string> RequestedPaths { get; } = [];

        public Task<SftpDownloadResult> DownloadAsync(
            Guid connectionProfileId,
            string remotePath,
            string localPath,
            CancellationToken cancellationToken = default)
            => DownloadAsync(connectionProfileId, remotePath, localPath, progress: null, cancellationToken);

        public Task<SftpDownloadResult> DownloadAsync(
            Guid connectionProfileId,
            string remotePath,
            string localPath,
            IProgress<long>? progress,
            CancellationToken cancellationToken = default)
        {
            RequestedPaths.Add(remotePath);
            return Task.FromResult(_resultFactory(remotePath));
        }
    }

    private sealed class FakeRunRepository : ISporeSyncRunRepository
    {
        public Task<SporeSyncRun> RecalculateAggregatesAsync(Guid runId, CancellationToken cancellationToken = default)
            => Task.FromResult(CreateRun(runId, "downloading"));

        public Task<bool> HasPendingDownloadsAsync(Guid runId, CancellationToken cancellationToken = default)
            => Task.FromResult(true);

        public Task<SporeSyncRun> UpdateStatusAsync(UpdateSporeSyncRunStatus update, CancellationToken cancellationToken = default)
            => Task.FromResult(CreateRun(update.Id, update.Status));

        private static SporeSyncRun CreateRun(Guid runId, string status)
        {
            return new SporeSyncRun
            {
                Id = runId,
                JobId = JobId,
                JobName = "job",
                Status = status,
                StartedAt = DateTimeOffset.UtcNow,
                TotalFileCount = 1,
                CompletedFileCount = 0,
                SkippedFileCount = 0,
                FailedFileCount = 0,
                TotalBytes = 100,
                DownloadedBytes = 0
            };
        }

        public Task<PagedResult<SporeSyncRun>> GetRunsAsync(RunQuery query, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<SporeSyncRun?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<SporeSyncRun> CreateAsync(Guid jobId, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<bool> HasActiveRunAsync(Guid jobId, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
    }

    private sealed class FakeJobRepository : ISporeSyncJobRepository
    {
        public Task<SporeSyncJob?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
        {
            return Task.FromResult<SporeSyncJob?>(new SporeSyncJob
            {
                Id = id,
                ConnectionProfileId = Guid.NewGuid(),
                Name = "job",
                SourcePath = "/remote",
                DestinationPath = "/data",
                PollingIntervalSeconds = 120,
                IsEnabled = true
            });
        }

        public Task<IReadOnlyCollection<SporeSyncJob>> GetAllAsync(CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<SporeSyncJob> UpsertAsync(UpsertSporeSyncJob job, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<IReadOnlyCollection<SporeSyncJob>> GetDueJobsAsync(CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task MarkPolledAsync(Guid id, CancellationToken cancellationToken = default)
            => Task.CompletedTask;
    }

    private sealed class FakeNotifier : ISyncDashboardNotifier
    {
        public Task NotifyRunUpdatedAsync(SporeSyncRun run, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task NotifyQueueItemUpdatedAsync(DownloadQueueItem item, CancellationToken cancellationToken = default)
            => Task.CompletedTask;
    }
}
