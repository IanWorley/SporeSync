using Microsoft.Extensions.Options;
using SporeSync.Business;
using SporeSync.Business.Security;
using SporeSync.Business.Service;
using SporeSync.Domain.Interface;
using SporeSync.Domain.Model;

namespace SporeSync.Business.Tests;

public sealed class SporeSyncJobServiceTests
{
    [Fact]
    public async Task GetConfiguredJobsAsync_DelegatesToRepository()
    {
        var repository = new RecordingSporeSyncJobRepository();
        var service = CreateService(repository);
        var cancellationToken = new CancellationTokenSource().Token;

        var result = await service.GetConfiguredJobsAsync(cancellationToken);

        Assert.Same(repository.Jobs, result);
        Assert.Equal(cancellationToken, repository.LastCancellationToken);
    }

    [Fact]
    public async Task GetByIdAsync_DelegatesToRepository()
    {
        var repository = new RecordingSporeSyncJobRepository();
        var service = CreateService(repository);
        var jobId = Guid.NewGuid();
        var cancellationToken = new CancellationTokenSource().Token;

        var result = await service.GetByIdAsync(jobId, cancellationToken);

        Assert.Same(repository.JobById, result);
        Assert.Equal(jobId, repository.LastRequestedId);
        Assert.Equal(cancellationToken, repository.LastCancellationToken);
    }

    [Fact]
    public async Task UpsertAsync_DelegatesToRepository()
    {
        var repository = new RecordingSporeSyncJobRepository();
        var service = CreateService(repository);
        var cancellationToken = new CancellationTokenSource().Token;
        var destinationPath = Path.Combine(TestDestinationRoot, "incoming");
        var job = new UpsertSporeSyncJob
        {
            ConnectionProfileId = Guid.NewGuid(),
            Name = "sync incoming",
            SourcePath = "/incoming",
            DestinationPath = destinationPath
        };

        var result = await service.UpsertAsync(job, cancellationToken);

        Assert.NotSame(repository.UpsertedJob, job);
        Assert.Equal(destinationPath, repository.UpsertedJob?.DestinationPath);
        Assert.Same(repository.UpsertResult, result);
        Assert.Equal(cancellationToken, repository.LastCancellationToken);
    }

    [Fact]
    public async Task UpsertAsync_ThrowsAndSkipsRepository_WhenDestinationEscapesRoot()
    {
        var repository = new RecordingSporeSyncJobRepository();
        var service = CreateService(repository);
        var job = new UpsertSporeSyncJob
        {
            ConnectionProfileId = Guid.NewGuid(),
            Name = "sync incoming",
            SourcePath = "/incoming",
            DestinationPath = Path.Combine(TestDestinationRoot, "..", "outside")
        };

        var exception = await Assert.ThrowsAsync<ArgumentException>(() => service.UpsertAsync(job));

        Assert.Contains("configured destination root", exception.Message);
        Assert.Null(repository.UpsertedJob);
    }

    private static string TestDestinationRoot =>
        Path.Combine(Path.GetTempPath(), "sporesync-destination-root");

    private static SporeSyncJobService CreateService(RecordingSporeSyncJobRepository repository)
    {
        var sandbox = new LocalDestinationPathSandbox(Options.Create(new SporeSyncOptions
        {
            DestinationRootPath = TestDestinationRoot
        }));

        return new SporeSyncJobService(repository, sandbox);
    }

    private sealed class RecordingSporeSyncJobRepository : ISporeSyncJobRepository
    {
        public IReadOnlyCollection<SporeSyncJob> Jobs { get; } =
        [
            new SporeSyncJob
            {
                Id = Guid.NewGuid(),
                ConnectionProfileId = Guid.NewGuid(),
                Name = "sync incoming",
                SourcePath = "/incoming",
                DestinationPath = "/local/incoming",
                PollingIntervalSeconds = 120,
                IsEnabled = true
            }
        ];

        public SporeSyncJob JobById { get; } = new()
        {
            Id = Guid.NewGuid(),
            ConnectionProfileId = Guid.NewGuid(),
            Name = "sync archive",
            SourcePath = "/archive",
            DestinationPath = "/local/archive",
            PollingIntervalSeconds = 300,
            IsEnabled = false
        };

        public SporeSyncJob UpsertResult { get; } = new()
        {
            Id = Guid.NewGuid(),
            ConnectionProfileId = Guid.NewGuid(),
            Name = "sync incoming",
            SourcePath = "/incoming",
            DestinationPath = "/local/incoming",
            PollingIntervalSeconds = 120,
            IsEnabled = true
        };

        public Guid? LastRequestedId { get; private set; }

        public CancellationToken LastCancellationToken { get; private set; }

        public UpsertSporeSyncJob? UpsertedJob { get; private set; }

        public Task<IReadOnlyCollection<SporeSyncJob>> GetAllAsync(CancellationToken cancellationToken = default)
        {
            LastCancellationToken = cancellationToken;
            return Task.FromResult(Jobs);
        }

        public Task<SporeSyncJob?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
        {
            LastRequestedId = id;
            LastCancellationToken = cancellationToken;
            return Task.FromResult<SporeSyncJob?>(JobById);
        }

        public Task<SporeSyncJob> UpsertAsync(
            UpsertSporeSyncJob job,
            CancellationToken cancellationToken = default)
        {
            UpsertedJob = job;
            LastCancellationToken = cancellationToken;
            return Task.FromResult(UpsertResult);
        }

        public Task<IReadOnlyCollection<SporeSyncJob>> GetDueJobsAsync(CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyCollection<SporeSyncJob>>(Array.Empty<SporeSyncJob>());

        public Task MarkPolledAsync(Guid id, CancellationToken cancellationToken = default)
            => Task.CompletedTask;
    }
}
