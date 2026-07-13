using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Renci.SshNet;
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
    private static readonly Guid ProfileId = Guid.NewGuid();

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
    public async Task ProcessNextItem_PermanentFailure_FailsWithoutSchedulingRetry()
    {
        var item = CreateItem("/remote/file.txt");
        var queueRepository = new FakeQueueRepository(item);
        var downloader = new FakeDownloader(_ => SftpDownloadResult.PermanentFailure("unsafe destination"));
        var worker = CreateWorker(queueRepository, downloader);

        await worker.ProcessNextItemAsync(CancellationToken.None);

        Assert.Empty(queueRepository.FailureCalls);
        var update = Assert.Single(queueRepository.ProgressUpdates);
        Assert.Equal("failed", update.Status);
        Assert.Equal("permanent_error", update.HandledReason);
        Assert.Equal("unsafe destination", update.ErrorMessage);
    }

    [Fact]
    public async Task StopAsync_CancelsPollWhileRetryRemainsScheduled()
    {
        var item = CreateItem("/remote/file.txt");
        var queueRepository = new FakeQueueRepository(item);
        var downloader = new FakeDownloader(_ => SftpDownloadResult.Failure("connection reset"));
        var worker = CreateWorker(queueRepository, downloader);

        await worker.StartAsync(CancellationToken.None);
        for (var attempt = 0; attempt < 100 && queueRepository.FailureCalls.Count == 0; attempt++)
        {
            await Task.Delay(10);
        }

        Assert.Single(queueRepository.FailureCalls);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(1));
        await worker.StopAsync(timeout.Token);
    }

    [Fact]
    public async Task ProcessNextItem_GroupWithFailedLeaf_MarksLeafFailedAndRecordsGroupFailure()
    {
        var group = CreateItem("/remote/reports/", isGroup: true, childCount: 2);
        var completedLeaf = CreateItem(
            "/remote/reports/done.txt",
            groupRemotePath: "/remote/reports/",
            status: "completed",
            bytesDownloaded: 100);
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
    public async Task ProcessNextItem_GroupWithPermanentLeafFailure_FailsGroupWithoutRetry()
    {
        var group = CreateItem("/remote/reports/", isGroup: true, childCount: 1);
        var leaf = CreateItem("/remote/reports/unsafe.txt", groupRemotePath: group.RemotePath);
        var queueRepository = new FakeQueueRepository(group) { Leaves = [leaf] };
        var downloader = new FakeDownloader(_ => SftpDownloadResult.PermanentFailure("unsafe destination"));
        var worker = CreateWorker(queueRepository, downloader);

        await worker.ProcessNextItemAsync(CancellationToken.None);

        Assert.Empty(queueRepository.FailureCalls);
        var groupUpdate = queueRepository.ProgressUpdates.Single(update => update.Id == group.Id);
        Assert.Equal("failed", groupUpdate.Status);
        Assert.Equal("permanent_error", groupUpdate.HandledReason);
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
        var completedLeaf = CreateItem(
            "/remote/reports/done.txt",
            groupRemotePath: "/remote/reports/",
            status: "completed",
            bytesDownloaded: 100);
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

    [Fact]
    public async Task ProcessNextItem_GroupWithMixedLeaves_ReusesOneConnectionAndSkipsCompletedAndSkippedLeaves()
    {
        var group = CreateItem("/remote/reports/", isGroup: true, fileSizeBytes: 600, childCount: 4);
        var completedLeaf = CreateItem(
            "/remote/reports/a.txt",
            groupRemotePath: group.RemotePath,
            status: "completed",
            fileSizeBytes: 100,
            bytesDownloaded: 100);
        var queuedLeaf1 = CreateItem("/remote/reports/b.txt", groupRemotePath: group.RemotePath, fileSizeBytes: 200);
        var queuedLeaf2 = CreateItem("/remote/reports/c.txt", groupRemotePath: group.RemotePath, fileSizeBytes: 300);
        var skippedLeaf = CreateItem(
            "/remote/reports/gone.txt",
            groupRemotePath: group.RemotePath,
            status: "skipped",
            fileSizeBytes: 50);
        var queueRepository = new FakeQueueRepository(group)
        {
            Leaves = [completedLeaf, queuedLeaf1, queuedLeaf2, skippedLeaf]
        };
        var clientFactory = new CountingSftpClientFactory();
        var downloader = new FakeDownloader(remotePath =>
            remotePath == queuedLeaf1.RemotePath
                ? SftpDownloadResult.Succeed(200, 1000)
                : SftpDownloadResult.Succeed(300, 1000));
        var worker = CreateWorker(queueRepository, downloader, clientFactory: clientFactory);

        var processed = await worker.ProcessNextItemAsync(CancellationToken.None);

        Assert.True(processed);
        Assert.Equal(1, clientFactory.ConnectCalls);
        Assert.True(clientFactory.LastConnection!.Disposed);
        Assert.Equal([queuedLeaf1.RemotePath, queuedLeaf2.RemotePath], downloader.RequestedPaths);
        Assert.Single(downloader.ConnectionsUsed.Distinct());

        var groupUpdate = queueRepository.ProgressUpdates.Single(update => update.Id == group.Id && update.Status == "completed");
        Assert.Equal(600, groupUpdate.BytesDownloaded);
    }

    [Fact]
    public async Task ProcessNextItem_GroupLeafDownload_RenewsLeafLeaseBeforeRecoverySweep()
    {
        var group = CreateItem("/remote/reports/", isGroup: true, childCount: 1);
        var leaf = CreateItem("/remote/reports/large.bin", groupRemotePath: group.RemotePath, status: "queued");
        var queueRepository = new FakeQueueRepository(group)
        {
            Leaves = [leaf]
        };
        var downloader = new BlockingDownloader();
        var worker = CreateWorker(queueRepository, downloader, downloadLeaseSeconds: 1);
        var recovery = new QueueRecoveryHostedService(
            BuildRecoveryScopeFactory(queueRepository),
            Options.Create(new SporeSyncOptions()),
            NullLogger<QueueRecoveryHostedService>.Instance);

        var processing = worker.ProcessNextItemAsync(CancellationToken.None);
        await downloader.WaitUntilStartedAsync();

        // The sweep occurs after the original one-second leaf lease expired.
        await Task.Delay(TimeSpan.FromMilliseconds(1500));
        await recovery.SweepAsync(CancellationToken.None);

        Assert.DoesNotContain(leaf.Id, queueRepository.RequeuedIds);

        downloader.Complete(SftpDownloadResult.Succeed(100, 50));
        Assert.True(await processing);
    }

    [Fact]
    public async Task ProcessNextItem_ReclaimedGroup_LeavesAnotherWorkersLiveLeafAlone()
    {
        // Worker A still owns this child. Worker B reclaimed only the parent
        // after its lease expired, so it must not renew, download, or complete
        // the child on Worker A's behalf.
        var reclaimedGroup = CreateItem("/remote/reports/", isGroup: true, childCount: 1);
        var liveLeafOwnedByWorkerA = CreateItem(
            "/remote/reports/large.bin",
            groupRemotePath: reclaimedGroup.RemotePath,
            status: "downloading");
        var queueRepository = new FakeQueueRepository(reclaimedGroup)
        {
            Leaves = [liveLeafOwnedByWorkerA]
        };
        var downloader = new FakeDownloader(_ => SftpDownloadResult.Succeed(100, 50));
        var workerB = CreateWorker(queueRepository, downloader);

        var processed = await workerB.ProcessNextItemAsync(CancellationToken.None);

        Assert.True(processed);
        Assert.Empty(downloader.RequestedPaths);
        Assert.DoesNotContain(queueRepository.ProgressUpdates, update => update.Id == liveLeafOwnedByWorkerA.Id);
        Assert.Equal([reclaimedGroup.Id], queueRepository.ReleasedIds);
    }

    [Fact]
    public async Task ProcessNextItem_GroupConnectFailure_MarksLeavesFailedAndRecordsGroupFailure()
    {
        var group = CreateItem("/remote/reports/", isGroup: true, fileSizeBytes: 300, childCount: 2);
        var leaf1 = CreateItem("/remote/reports/a.txt", groupRemotePath: group.RemotePath, fileSizeBytes: 100);
        var leaf2 = CreateItem("/remote/reports/b.txt", groupRemotePath: group.RemotePath, fileSizeBytes: 200);
        var queueRepository = new FakeQueueRepository(group)
        {
            Leaves = [leaf1, leaf2]
        };
        var clientFactory = new CountingSftpClientFactory { FailConnects = true };
        var downloader = new FakeDownloader(_ => SftpDownloadResult.Succeed(100, null));
        var worker = CreateWorker(queueRepository, downloader, clientFactory: clientFactory);

        var processed = await worker.ProcessNextItemAsync(CancellationToken.None);

        Assert.True(processed);
        Assert.Equal(2, clientFactory.ConnectCalls);
        Assert.Empty(downloader.RequestedPaths);
        Assert.Equal("failed", queueRepository.ProgressUpdates.Single(update => update.Id == leaf1.Id).Status);
        Assert.Equal("failed", queueRepository.ProgressUpdates.Single(update => update.Id == leaf2.Id).Status);

        var failure = Assert.Single(queueRepository.FailureCalls);
        Assert.Equal(group.Id, failure.Id);
        Assert.Equal(0, failure.BytesDownloaded);
    }

    private static DownloadWorkerHostedService CreateWorker(
        FakeQueueRepository queueRepository,
        ISftpFileDownloader downloader,
        int maxRetries = 3,
        int baseDelaySeconds = 30,
        int stabilityWindowSeconds = 15,
        int downloadLeaseSeconds = 300,
        CountingSftpClientFactory? clientFactory = null)
    {
        var options = Options.Create(new SporeSyncOptions
        {
            DownloadPollIntervalMs = 600_000,
            DownloadMaxRetries = maxRetries,
            DownloadRetryBaseDelaySeconds = baseDelaySeconds,
            DownloadRetryJitterRatio = 0,
            RemoteFileStabilityWindowSeconds = stabilityWindowSeconds,
            DownloadLeaseSeconds = downloadLeaseSeconds
        });

        var services = new ServiceCollection();
        services.AddSingleton<IDownloadQueueItemRepository>(queueRepository);
        services.AddSingleton<ISporeSyncRunRepository>(new FakeRunRepository());
        services.AddSingleton<ISporeSyncJobRepository>(new FakeJobRepository());
        services.AddSingleton<ISftpFileDownloader>(downloader);
        services.AddSingleton<ISftpClientFactory>(clientFactory ?? new CountingSftpClientFactory());
        services.AddSingleton<ISyncDashboardNotifier>(new FakeNotifier());
        var provider = services.BuildServiceProvider();

        return new DownloadWorkerHostedService(
            provider.GetRequiredService<IServiceScopeFactory>(),
            options,
            new SporeSyncMetrics(),
            new DownloadRetryPolicy(options),
            NullLogger<DownloadWorkerHostedService>.Instance);
    }

    private static IServiceScopeFactory BuildRecoveryScopeFactory(FakeQueueRepository queueRepository)
    {
        var services = new ServiceCollection();
        services.AddSingleton<IDownloadQueueItemRepository>(queueRepository);
        services.AddSingleton<ISporeSyncRunRepository>(new FakeRunRepository());
        services.AddSingleton<ISyncDashboardNotifier>(new FakeNotifier());
        return services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>();
    }

    private static DownloadQueueItem CreateItem(
        string remotePath,
        bool isGroup = false,
        int childCount = 0,
        string? groupRemotePath = null,
        string status = "queued",
        long bytesDownloaded = 0,
        int retryCount = 0,
        long fileSizeBytes = 100)
    {
        return new DownloadQueueItem
        {
            Id = Guid.NewGuid(),
            JobId = JobId,
            SyncRunId = RunId,
            RemotePath = remotePath,
            DestinationPath = "/data" + remotePath.TrimEnd('/'),
            FileSizeBytes = fileSizeBytes,
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
        private readonly Dictionary<Guid, DateTimeOffset> _leaseExpiresAt = [];

        public FakeQueueRepository(DownloadQueueItem claimable)
        {
            _claimable = claimable;
        }

        public IReadOnlyList<DownloadQueueItem> Leaves { get; init; } = [];

        public List<UpdateDownloadQueueItemProgress> ProgressUpdates { get; } = [];

        public List<FailureCall> FailureCalls { get; } = [];

        public List<DeferCall> DeferCalls { get; } = [];

        public List<Guid> RequeuedIds { get; } = [];

        public List<Guid> ReleasedIds { get; } = [];

        public Task<DownloadQueueItem?> ClaimNextAsync(int leaseSeconds, CancellationToken cancellationToken = default)
        {
            var item = _claimable;
            _claimable = null;
            if (item is not null)
            {
                _leaseExpiresAt[item.Id] = DateTimeOffset.UtcNow.AddSeconds(leaseSeconds);
            }

            return Task.FromResult(item);
        }

        public Task<DownloadQueueItem?> ClaimGroupLeafAsync(
            Guid id,
            Guid runId,
            string groupRemotePath,
            int leaseSeconds,
            CancellationToken cancellationToken = default)
        {
            var leaf = Leaves.FirstOrDefault(leaf =>
                leaf.Id == id
                && leaf.SyncRunId == runId
                && leaf.GroupRemotePath == groupRemotePath
                && (string.Equals(leaf.Status, "queued", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(leaf.Status, "failed", StringComparison.OrdinalIgnoreCase)));

            if (leaf is not null)
            {
                _leaseExpiresAt[leaf.Id] = DateTimeOffset.UtcNow.AddSeconds(leaseSeconds);
            }

            return Task.FromResult(leaf);
        }

        public Task<bool> RenewLeaseAsync(Guid id, int leaseSeconds, CancellationToken cancellationToken = default)
        {
            if (!_leaseExpiresAt.ContainsKey(id))
            {
                return Task.FromResult(false);
            }

            _leaseExpiresAt[id] = DateTimeOffset.UtcNow.AddSeconds(leaseSeconds);
            return Task.FromResult(true);
        }

        public Task<DownloadQueueItem?> ReleaseAsync(Guid id, CancellationToken cancellationToken = default)
        {
            ReleasedIds.Add(id);
            return Task.FromResult<DownloadQueueItem?>(CreateResult(id, "queued", 0));
        }

        public Task<IReadOnlyList<DownloadQueueItem>> RequeueStaleAsync(
            CancellationToken cancellationToken = default)
        {
            var staleIds = _leaseExpiresAt
                .Where(entry => entry.Value <= DateTimeOffset.UtcNow)
                .Select(entry => entry.Key)
                .ToArray();
            var requeued = new List<DownloadQueueItem>();

            foreach (var id in staleIds)
            {
                var leaf = Leaves.FirstOrDefault(item => item.Id == id);
                if (leaf is not null)
                {
                    RequeuedIds.Add(id);
                    requeued.Add(leaf);
                }

                _leaseExpiresAt.Remove(id);
            }

            return Task.FromResult<IReadOnlyList<DownloadQueueItem>>(requeued);
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
            return Task.FromResult(CreateResult(update.Id, update.Status, update.BytesDownloaded));
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
            return Task.FromResult(CreateResult(id, "queued", bytesDownloaded ?? 0));
        }

        public Task<DownloadQueueItem> DeferAsync(
            Guid id,
            DateTimeOffset nextAttemptAt,
            string reason,
            long? bytesDownloaded = null,
            CancellationToken cancellationToken = default)
        {
            DeferCalls.Add(new DeferCall(id, nextAttemptAt, reason, bytesDownloaded));
            return Task.FromResult(CreateResult(id, "queued", bytesDownloaded ?? 0));
        }

        private static DownloadQueueItem CreateResult(Guid id, string status, long bytesDownloaded)
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
                BytesDownloaded = bytesDownloaded,
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
        {
            if (_claimable?.Id == id)
            {
                return Task.FromResult<DownloadQueueItem?>(_claimable);
            }

            return Task.FromResult<DownloadQueueItem?>(Leaves.FirstOrDefault(leaf => leaf.Id == id));
        }

        public Task<DownloadQueueItem> UpsertAsync(UpsertDownloadQueueItem item, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<IReadOnlyList<DownloadQueueItem>> UpsertManyAsync(
            IReadOnlyCollection<UpsertDownloadQueueItem> items,
            CancellationToken cancellationToken = default)
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

        public List<IConnectedSftpClient> ConnectionsUsed { get; } = [];

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

        public Task<SftpDownloadResult> DownloadAsync(
            IConnectedSftpClient connection,
            string remotePath,
            string localPath,
            IProgress<long>? progress = null,
            CancellationToken cancellationToken = default)
        {
            ConnectionsUsed.Add(connection);
            return DownloadAsync(ProfileId, remotePath, localPath, progress, cancellationToken);
        }
    }

    private sealed class BlockingDownloader : ISftpFileDownloader
    {
        private readonly TaskCompletionSource _started = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource<SftpDownloadResult> _completion = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task WaitUntilStartedAsync() => _started.Task;

        public void Complete(SftpDownloadResult result) => _completion.TrySetResult(result);

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
            _started.TrySetResult();
            return _completion.Task;
        }

        public Task<SftpDownloadResult> DownloadAsync(
            IConnectedSftpClient connection,
            string remotePath,
            string localPath,
            IProgress<long>? progress = null,
            CancellationToken cancellationToken = default)
            => DownloadAsync(ProfileId, remotePath, localPath, progress, cancellationToken);
    }

    private sealed class CountingSftpClientFactory : ISftpClientFactory
    {
        public int ConnectCalls { get; private set; }

        public bool FailConnects { get; init; }

        public FakeConnectedSftpClient? LastConnection { get; private set; }

        public Task<IConnectedSftpClient> ConnectAsync(
            Guid connectionProfileId,
            CancellationToken cancellationToken = default)
        {
            ConnectCalls++;
            if (FailConnects)
            {
                throw new InvalidOperationException("SFTP host unreachable.");
            }

            LastConnection = new FakeConnectedSftpClient();
            return Task.FromResult<IConnectedSftpClient>(LastConnection);
        }
    }

    private sealed class FakeConnectedSftpClient : IConnectedSftpClient
    {
        public SftpClient Client => throw new NotSupportedException("Fake connection has no real client.");

        public bool IsConnected => !Disposed;

        public bool Disposed { get; private set; }

        public ValueTask DisposeAsync()
        {
            Disposed = true;
            return ValueTask.CompletedTask;
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
            => Task.FromResult<SporeSyncRun?>(CreateRun(id, "downloading"));

        public Task<SporeSyncRun?> TryCreateAsync(
            Guid jobId,
            int leaseSeconds = 1800,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<bool> HasActiveRunAsync(Guid jobId, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<SyncHistoryPruneResult> PruneHistoryAsync(
            DateTimeOffset cutoff,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<bool> RenewLeaseAsync(Guid runId, int leaseSeconds, CancellationToken cancellationToken = default)
            => Task.FromResult(true);

        public Task<IReadOnlyList<SporeSyncRun>> ReapOrphanedAsync(
            CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<SporeSyncRun>>([]);

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

    private sealed class FakeJobRepository : ISporeSyncJobRepository
    {
        public Task<SporeSyncJob?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
        {
            return Task.FromResult<SporeSyncJob?>(new SporeSyncJob
            {
                Id = id,
                ConnectionProfileId = ProfileId,
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

        public Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<SafeDeleteSporeSyncJobResult> SafeDeleteAsync(
            Guid id,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<int> CountByConnectionProfileAsync(Guid connectionProfileId, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
    }

    private sealed class FakeNotifier : ISyncDashboardNotifier
    {
        public Task NotifyRunUpdatedAsync(SporeSyncRun run, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task NotifyQueueItemUpdatedAsync(DownloadQueueItem item, CancellationToken cancellationToken = default)
            => Task.CompletedTask;
    }
}
