using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SporeSync.Business.Interface;
using SporeSync.Business.Service;
using SporeSync.Business.Worker;
using SporeSync.Domain.Interface;
using SporeSync.Domain.Model;
namespace SporeSync.Business.Tests.Worker;
public sealed class ManualRunHostedServiceTests
{
    private static readonly Guid JobId = Guid.NewGuid();
    private static readonly Guid RunId = Guid.NewGuid();
    [Fact]
    public async Task ProcessWorkItem_CreatesAndDisposesScope()
    {
        var harness = new WorkerHarness((_, _, _) => Task.CompletedTask);
        await harness.Worker.ProcessWorkItemAsync(new ManualRunWorkItem(JobId, RunId), CancellationToken.None);
        Assert.Equal(1, harness.ScanCalls);
        Assert.Equal(1, harness.DisposalCount);
    }
    [Fact]
    public async Task ProcessWorkItem_ShutdownTokenCancelsScanAndTerminatesRun()
    {
        CancellationToken receivedToken = default;
        var harness = new WorkerHarness((_, _, token) =>
        {
            receivedToken = token;
            throw new OperationCanceledException(token);
        });
        using var stopping = new CancellationTokenSource();
        stopping.Cancel();
        await harness.Worker.ProcessWorkItemAsync(new ManualRunWorkItem(JobId, RunId), stopping.Token);
        Assert.Equal(stopping.Token, receivedToken);
        Assert.Equal("cancelled", Assert.Single(harness.TerminalUpdates).Status);
    }
    [Fact]
    public async Task ProcessWorkItem_UnexpectedFailureTerminatesRun()
    {
        var harness = new WorkerHarness((_, _, _) => throw new InvalidOperationException("scan exploded"));
        await harness.Worker.ProcessWorkItemAsync(new ManualRunWorkItem(JobId, RunId), CancellationToken.None);
        var update = Assert.Single(harness.TerminalUpdates);
        Assert.Equal("failed", update.Status);
        Assert.Equal("scan exploded", update.ErrorMessage);
    }
    [Fact]
    public async Task HostedWorker_RenewsSecondRunWhileFirstRunsPastLease()
    {
        TaskCompletionSource firstStarted = new(TaskCreationOptions.RunContinuationsAsynchronously),
            releaseFirst = new(TaskCreationOptions.RunContinuationsAsynchronously);
        var secondStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var scanNumber = 0;
        var harness = new WorkerHarness(async (_, _, token) =>
        {
            if (Interlocked.Increment(ref scanNumber) == 1)
            {
                firstStarted.SetResult();
                await releaseFirst.Task.WaitAsync(token);
            }
            else secondStarted.SetResult();
        }, leaseSeconds: 2);
        var waitingRunId = Guid.NewGuid();
        harness.Enqueue(new ManualRunWorkItem(JobId, RunId));
        harness.Enqueue(new ManualRunWorkItem(JobId, waitingRunId));
        await harness.Worker.StartAsync(CancellationToken.None);
        await firstStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));
        await Task.Delay(TimeSpan.FromMilliseconds(2200));
        Assert.Contains(waitingRunId, harness.RenewedRunIds);
        releaseFirst.SetResult();
        await secondStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));
        await harness.Worker.StopAsync(CancellationToken.None);
    }
    [Fact]
    public async Task TriggerManualRun_WhenQueueIsFull_DoesNotCreateRun()
    {
        var queue = new ManualRunQueue(Options.Create(new SporeSyncOptions { ManualRunQueueCapacity = 1 }));
        Assert.True(queue.TryReserve(out var occupied));
        occupied!.Enqueue(new ManualRunWorkItem(Guid.NewGuid(), Guid.NewGuid()));
        var createCalls = 0;
        var jobRepository = Proxy<ISporeSyncJobRepository>((method, _) => method.Name switch
        {
            nameof(ISporeSyncJobRepository.GetByIdAsync) => Task.FromResult<SporeSyncJob?>(CreateJob()),
            _ => throw new NotSupportedException(method.Name)
        });
        var runRepository = Proxy<ISporeSyncRunRepository>((method, _) =>
        {
            if (method.Name == nameof(ISporeSyncRunRepository.TryCreateAsync))
            {
                createCalls++;
            }
            throw new NotSupportedException(method.Name);
        });
        var service = new SyncJobRunService(
            jobRepository,
            runRepository,
            queue,
            CreateNotifier(),
            Options.Create(new SporeSyncOptions()));
        var result = await service.TriggerManualRunAsync(JobId);
        Assert.Equal(SyncJobRunError.QueueSaturated, result.Error);
        Assert.Equal(0, createCalls);
    }
    private sealed class WorkerHarness
    {
        public WorkerHarness(
            Func<SporeSyncJob, SporeSyncRun, CancellationToken, Task> scan,
            int leaseSeconds = 1800)
        {
            var services = new ServiceCollection();
            services.AddSingleton(Proxy<ISporeSyncJobRepository>((method, _) => method.Name switch
            {
                nameof(ISporeSyncJobRepository.GetByIdAsync) => Task.FromResult<SporeSyncJob?>(CreateJob()),
                _ => throw new NotSupportedException(method.Name)
            }));
            services.AddSingleton(Proxy<ISporeSyncRunRepository>((method, args) => method.Name switch
            {
                nameof(ISporeSyncRunRepository.GetByIdAsync) => (object)Task.FromResult<SporeSyncRun?>(CreateRun()),
                nameof(ISporeSyncRunRepository.UpdateStatusAsync) => RecordUpdate((UpdateSporeSyncRunStatus)args![0]!),
                nameof(ISporeSyncRunRepository.RenewLeaseAsync) => RecordRenewal((Guid)args![0]!),
                _ => throw new NotSupportedException(method.Name)
            }));
            services.AddSingleton(CreateNotifier());
            services.AddScoped(_ => new ScopeMarker(() => DisposalCount++));
            services.AddScoped<ISyncRunOrchestrator>(provider => new TestOrchestrator(
                provider.GetRequiredService<ScopeMarker>(),
                (job, run, token) =>
                {
                    ScanCalls++;
                    return scan(job, run, token);
                }));
            Provider = services.BuildServiceProvider();
            Queue = new ManualRunQueue(Options.Create(new SporeSyncOptions { ManualRunQueueCapacity = 2 }));
            Worker = new ManualRunHostedService(
                Queue,
                Provider.GetRequiredService<IServiceScopeFactory>(),
                Options.Create(new SporeSyncOptions { RunScanLeaseSeconds = leaseSeconds }),
                NullLogger<ManualRunHostedService>.Instance);
        }
        public ServiceProvider Provider { get; }
        public ManualRunHostedService Worker { get; }
        public ManualRunQueue Queue { get; }
        public List<UpdateSporeSyncRunStatus> TerminalUpdates { get; } = [];
        public System.Collections.Concurrent.ConcurrentQueue<Guid> RenewedRunIds { get; } = [];
        public int ScanCalls { get; private set; }
        public int DisposalCount { get; private set; }
        private Task<SporeSyncRun> RecordUpdate(UpdateSporeSyncRunStatus update)
        {
            TerminalUpdates.Add(update);
            return Task.FromResult(CreateRun(update.Status));
        }
        public void Enqueue(ManualRunWorkItem item)
        {
            Assert.True(Queue.TryReserve(out var reservation));
            reservation!.Enqueue(item);
        }
        private Task<bool> RecordRenewal(Guid runId)
        {
            RenewedRunIds.Enqueue(runId);
            return Task.FromResult(true);
        }
    }
    private sealed class ScopeMarker(Action dispose) : IDisposable { public void Dispose() => dispose(); }
    private sealed class TestOrchestrator(
        ScopeMarker marker,
        Func<SporeSyncJob, SporeSyncRun, CancellationToken, Task> scan) : ISyncRunOrchestrator
    {
        public async Task<SporeSyncRun> ScanAsync(SporeSyncJob job, SporeSyncRun run,
            CancellationToken cancellationToken = default)
        {
            GC.KeepAlive(marker);
            await scan(job, run, cancellationToken);
            return run;
        }
    }
    private static SporeSyncJob CreateJob() => new() { Id = JobId, ConnectionProfileId = Guid.NewGuid(),
        Name = "manual", SourcePath = "/source", DestinationPath = "/destination", IsEnabled = true };
    private static SporeSyncRun CreateRun(string status = "queued") => new()
    {
        Id = RunId, JobId = JobId, JobName = "manual", Status = status,
        StartedAt = DateTimeOffset.UtcNow, TotalFileCount = 0, CompletedFileCount = 0,
        SkippedFileCount = 0, FailedFileCount = 0, TotalBytes = 0, DownloadedBytes = 0
    };
    private static ISyncDashboardNotifier CreateNotifier() => Proxy<ISyncDashboardNotifier>((method, _) =>
        method.Name is nameof(ISyncDashboardNotifier.NotifyRunUpdatedAsync) or
            nameof(ISyncDashboardNotifier.NotifyQueueItemUpdatedAsync) ? Task.CompletedTask :
            throw new NotSupportedException(method.Name));
    private static T Proxy<T>(Func<MethodInfo, object?[]?, object?> handler) where T : class
    {
        var proxy = DispatchProxy.Create<T, TestDispatchProxy>();
        ((TestDispatchProxy)(object)proxy).Handler = handler;
        return proxy;
    }
    private class TestDispatchProxy : DispatchProxy
    {
        public required Func<MethodInfo, object?[]?, object?> Handler { private get; set; }
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) => Handler(targetMethod!, args);
    }
}
