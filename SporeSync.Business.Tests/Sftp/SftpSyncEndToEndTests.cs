using System.Security.Cryptography;
using Microsoft.Extensions.DependencyInjection;
using SporeSync.Business.Interface;
using SporeSync.Business.Observability;
using SporeSync.Business.Security;
using SporeSync.Business.Service;
using SporeSync.Business.Sftp;
using SporeSync.Business.Worker;
using SporeSync.Domain.Interface;
using SporeSync.Domain.Model;
using SporeSync.Infrastructure.Repository;

namespace SporeSync.Business.Tests.Sftp;

/// <summary>
/// End-to-end SFTP sync pipeline tests against a real SFTP server (atmoz/sftp)
/// and a real PostgreSQL database (both via Testcontainers). Exercises
/// scan -> enqueue -> download -> run completion, including first-child opaque
/// folder grouping, change detection on re-scan, and remote-deletion handling.
/// </summary>
public sealed class SftpSyncEndToEndTests :
    IClassFixture<RepositoryTestcontainerFixture>,
    IClassFixture<SftpTestcontainerFixture>,
    IDisposable
{
    private const int MaxWorkerIterations = 25;

    private readonly SftpTestcontainerFixture _sftp;
    private readonly string _destinationRoot;
    private readonly ServiceProvider _provider;

    public SftpSyncEndToEndTests(
        RepositoryTestcontainerFixture database,
        SftpTestcontainerFixture sftp)
    {
        _sftp = sftp;
        _destinationRoot = Directory.CreateTempSubdirectory("sporesync-e2e-").FullName;

        var keyProvider = new EncryptionKeyProvider();
        keyProvider.Initialize(RandomNumberGenerator.GetBytes(32));

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(database.DataSource);
        services.Configure<SporeSyncOptions>(options =>
        {
            options.DestinationRootPath = _destinationRoot;
            options.DownloadPollIntervalMs = 50;
            options.SftpConnectionTimeoutSeconds = 30;
            options.SftpOperationTimeoutSeconds = 60;
            options.RemoteFileStabilityWindowSeconds = 0;
        });
        services.AddSingleton<IEncryptionKeyProvider>(keyProvider);
        services.AddSingleton<ISecretProtector, SecretProtector>();
        services.AddSingleton<LocalDestinationPathSandbox>();
        services.AddSingleton<SporeSyncMetrics>();
        services.AddSingleton<ISyncDashboardNotifier, NoOpSyncDashboardNotifier>();
        services.AddScoped<ISftpConnectionProfileRepository, SftpConnectionProfileRepository>();
        services.AddScoped<ISporeSyncJobRepository, SporeSyncJobRepository>();
        services.AddScoped<ISporeSyncRunRepository, SporeSyncRunRepository>();
        services.AddScoped<IDownloadQueueItemRepository, DownloadQueueItemRepository>();
        services.AddScoped<ISftpClientFactory, SftpClientFactory>();
        services.AddScoped<ISshHostKeyScanner, SshHostKeyScanner>();
        services.AddScoped<RealSftpDirectoryScanner>();
        services.AddScoped<ISftpFileDownloader, SftpFileDownloader>();
        services.AddSingleton<DownloadRetryPolicy>();
        services.AddScoped<IChangeDetector, ChangeDetector>();
        services.AddScoped<ISyncRunOrchestrator, SyncRunOrchestrator>();
        services.AddSingleton<DownloadWorkerHostedService>();

        _provider = services.BuildServiceProvider();
    }

    public void Dispose()
    {
        _provider.Dispose();
        if (Directory.Exists(_destinationRoot))
        {
            Directory.Delete(_destinationRoot, recursive: true);
        }
    }

    [Fact]
    public async Task FullPipeline_ScansEnqueuesDownloadsAndCompletes_WithFolderGrouping()
    {
        var caseRoot = $"{SftpTestcontainerFixture.RemoteRoot}/full-pipeline";
        await _sftp.WriteFileAsync($"{caseRoot}/loose.txt", "loose file contents");
        await _sftp.WriteFileAsync($"{caseRoot}/show-a/episode-01/video.mkv", "video bytes");
        await _sftp.WriteFileAsync($"{caseRoot}/show-a/episode-01/notes.nfo", "episode notes");

        using var scope = _provider.CreateScope();
        var runRepository = scope.ServiceProvider.GetRequiredService<ISporeSyncRunRepository>();
        var queueRepository = scope.ServiceProvider.GetRequiredService<IDownloadQueueItemRepository>();
        var orchestrator = scope.ServiceProvider.GetRequiredService<ISyncRunOrchestrator>();

        var job = await CreateJobAsync(scope.ServiceProvider, caseRoot, "full-pipeline");

        var run = await runRepository.TryCreateAsync(job.Id);
        run = await orchestrator.ScanAsync(job, run);

        Assert.Equal("downloading", run.Status);
        Assert.Equal(2, run.TotalFileCount);
        Assert.True(run.TotalBytes > 0);

        var queued = await queueRepository.GetByRunIdAsync(run.Id, new QueueItemQuery());
        Assert.Equal(2, queued.TotalCount);
        var group = Assert.Single(queued.Items, item => item.IsGroup);
        Assert.Equal(2, group.ChildCount);
        Assert.EndsWith("show-a/", group.RemotePath);
        Assert.Single(queued.Items, item => !item.IsGroup && item.RemotePath.EndsWith("loose.txt"));

        await DrainDownloadQueueAsync();

        var completedRun = await runRepository.GetByIdAsync(run.Id);
        Assert.NotNull(completedRun);
        Assert.Equal("completed", completedRun.Status);
        Assert.Equal(2, completedRun.CompletedFileCount);
        Assert.Equal(0, completedRun.FailedFileCount);
        Assert.True(completedRun.DownloadedBytes > 0);

        var destination = DestinationFor("full-pipeline");
        Assert.Equal("loose file contents", await File.ReadAllTextAsync(Path.Combine(destination, "loose.txt")));
        Assert.Equal("video bytes", await File.ReadAllTextAsync(Path.Combine(destination, "show-a", "episode-01", "video.mkv")));
        Assert.Equal("episode notes", await File.ReadAllTextAsync(Path.Combine(destination, "show-a", "episode-01", "notes.nfo")));

        var groupAfter = await queueRepository.GetByRunIdAsync(run.Id, new QueueItemQuery());
        Assert.All(groupAfter.Items, item => Assert.Equal("completed", item.Status));

        // Re-scan with no remote changes: nothing is enqueued and the run
        // completes immediately.
        var secondRun = await runRepository.TryCreateAsync(job.Id);
        secondRun = await orchestrator.ScanAsync(job, secondRun);
        Assert.Equal("completed", secondRun.Status);
        Assert.Equal(0, secondRun.TotalFileCount);
    }

    [Fact]
    public async Task ModifiedRemoteFile_IsReEnqueuedAndDownloadedAgain()
    {
        var caseRoot = $"{SftpTestcontainerFixture.RemoteRoot}/modified-file";
        await _sftp.WriteFileAsync($"{caseRoot}/report.csv", "version-1");

        using var scope = _provider.CreateScope();
        var runRepository = scope.ServiceProvider.GetRequiredService<ISporeSyncRunRepository>();
        var orchestrator = scope.ServiceProvider.GetRequiredService<ISyncRunOrchestrator>();

        var job = await CreateJobAsync(scope.ServiceProvider, caseRoot, "modified-file");

        var firstRun = await orchestrator.ScanAsync(job, await runRepository.TryCreateAsync(job.Id));
        Assert.Equal("downloading", firstRun.Status);
        await DrainDownloadQueueAsync();

        var localPath = Path.Combine(DestinationFor("modified-file"), "report.csv");
        Assert.Equal("version-1", await File.ReadAllTextAsync(localPath));

        await _sftp.WriteFileAsync($"{caseRoot}/report.csv", "version-2-with-more-content");

        var secondRun = await orchestrator.ScanAsync(job, await runRepository.TryCreateAsync(job.Id));
        Assert.Equal("downloading", secondRun.Status);
        Assert.Equal(1, secondRun.TotalFileCount);
        await DrainDownloadQueueAsync();

        var completedRun = await runRepository.GetByIdAsync(secondRun.Id);
        Assert.NotNull(completedRun);
        Assert.Equal("completed", completedRun.Status);
        Assert.Equal("version-2-with-more-content", await File.ReadAllTextAsync(localPath));
    }

    [Fact]
    public async Task RemoteDeletedFile_IsMarkedSkipped_AndLocalCopyIsKept()
    {
        var caseRoot = $"{SftpTestcontainerFixture.RemoteRoot}/remote-delete";
        await _sftp.WriteFileAsync($"{caseRoot}/keep.txt", "keep me");
        await _sftp.WriteFileAsync($"{caseRoot}/gone.txt", "delete me remotely");

        using var scope = _provider.CreateScope();
        var runRepository = scope.ServiceProvider.GetRequiredService<ISporeSyncRunRepository>();
        var queueRepository = scope.ServiceProvider.GetRequiredService<IDownloadQueueItemRepository>();
        var orchestrator = scope.ServiceProvider.GetRequiredService<ISyncRunOrchestrator>();

        var job = await CreateJobAsync(scope.ServiceProvider, caseRoot, "remote-delete");

        var firstRun = await orchestrator.ScanAsync(job, await runRepository.TryCreateAsync(job.Id));
        Assert.Equal("downloading", firstRun.Status);
        await DrainDownloadQueueAsync();

        await _sftp.DeleteFileAsync($"{caseRoot}/gone.txt");

        var secondRun = await orchestrator.ScanAsync(job, await runRepository.TryCreateAsync(job.Id));
        Assert.Equal("completed", secondRun.Status);
        Assert.Equal(1, secondRun.SkippedFileCount);

        var items = await queueRepository.GetByRunIdAsync(secondRun.Id, new QueueItemQuery());
        var skipped = Assert.Single(items.Items);
        Assert.Equal("skipped", skipped.Status);
        Assert.Equal("remote_deleted", skipped.HandledReason);
        Assert.EndsWith("gone.txt", skipped.RemotePath);

        // The local copy is retained even after the remote file disappears.
        var destination = DestinationFor("remote-delete");
        Assert.Equal("delete me remotely", await File.ReadAllTextAsync(Path.Combine(destination, "gone.txt")));
        Assert.Equal("keep me", await File.ReadAllTextAsync(Path.Combine(destination, "keep.txt")));
    }

    [Fact]
    public async Task HostKeyVerification_FailsClosedAndSupportsRotation()
    {
        using var scope = _provider.CreateScope();
        var services = scope.ServiceProvider;
        var factory = services.GetRequiredService<ISftpClientFactory>();
        var observed = (await services.GetRequiredService<ISshHostKeyScanner>()
            .ScanAsync(_sftp.Host, _sftp.Port)).FingerprintSha256;
        const string other = "SHA256:AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA";

        foreach (var trusted in new IReadOnlyList<string>[] { [], [other] })
        {
            var profile = await CreateProfileAsync(services, "rejected", trusted);
            var exception = await Assert.ThrowsAsync<SshHostKeyMismatchException>(
                () => factory.ConnectAsync(profile.Id));
            Assert.Equal(_sftp.Host, exception.Host);
            Assert.Equal(observed, exception.ActualFingerprint);
            Assert.DoesNotContain(SftpTestcontainerFixture.Password, exception.Message);
        }

        var rotating = await CreateProfileAsync(services, "rotation", [other, observed]);
        await using var connection = await factory.ConnectAsync(rotating.Id);
        Assert.True(connection.IsConnected);
    }

    private async Task<SporeSyncJob> CreateJobAsync(
        IServiceProvider services,
        string sourcePath,
        string caseName)
    {
        var jobRepository = services.GetRequiredService<ISporeSyncJobRepository>();
        var observed = (await services.GetRequiredService<ISshHostKeyScanner>()
            .ScanAsync(_sftp.Host, _sftp.Port)).FingerprintSha256;
        var profile = await CreateProfileAsync(services, caseName, [observed]);

        return await jobRepository.UpsertAsync(new UpsertSporeSyncJob
        {
            ConnectionProfileId = profile.Id,
            Name = $"job-e2e-{caseName}-{Guid.NewGuid():N}",
            SourcePath = sourcePath,
            DestinationPath = DestinationFor(caseName),
            PollingIntervalSeconds = 120,
            IsEnabled = true
        });
    }

    private async Task<SftpConnectionProfile> CreateProfileAsync(
        IServiceProvider services,
        string caseName,
        IReadOnlyList<string> trustedHostKeys)
    {
        return await services.GetRequiredService<ISftpConnectionProfileRepository>().UpsertAsync(
            new SftpConnectionProfile
            {
                Id = Guid.NewGuid(),
                Name = $"sftp-e2e-{caseName}-{Guid.NewGuid():N}",
                Host = _sftp.Host,
                Port = _sftp.Port,
                Username = SftpTestcontainerFixture.Username,
                EncryptedPassword = services.GetRequiredService<ISecretProtector>()
                    .Protect(SftpTestcontainerFixture.Password),
                TrustedHostKeyFingerprintsSha256 = trustedHostKeys,
                IsDefault = false
            });
    }

    private string DestinationFor(string caseName) => Path.Combine(_destinationRoot, caseName);

    private async Task DrainDownloadQueueAsync()
    {
        var worker = _provider.GetRequiredService<DownloadWorkerHostedService>();
        for (var iteration = 0; iteration < MaxWorkerIterations; iteration++)
        {
            if (!await worker.ProcessNextItemAsync(CancellationToken.None))
            {
                return;
            }
        }

        throw new InvalidOperationException(
            $"Download queue did not drain within {MaxWorkerIterations} worker iterations.");
    }

    private sealed class NoOpSyncDashboardNotifier : ISyncDashboardNotifier
    {
        public Task NotifyRunUpdatedAsync(SporeSyncRun run, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task NotifyQueueItemUpdatedAsync(DownloadQueueItem item, CancellationToken cancellationToken = default)
            => Task.CompletedTask;
    }
}
