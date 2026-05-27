using SftpSync.Business.Service;
using SftpSync.Domain.Interface;
using SftpSync.Domain.Model;

namespace SftpSync.Business.Tests;

public sealed class SftpSyncJobServiceTests
{
    [Fact]
    public async Task GetConfiguredJobsAsync_DelegatesToRepository()
    {
        var repository = new RecordingSftpSyncJobRepository();
        var service = new SftpSyncJobService(repository);
        var cancellationToken = new CancellationTokenSource().Token;

        var result = await service.GetConfiguredJobsAsync(cancellationToken);

        Assert.Same(repository.Jobs, result);
        Assert.Equal(cancellationToken, repository.LastCancellationToken);
    }

    [Fact]
    public async Task GetByIdAsync_DelegatesToRepository()
    {
        var repository = new RecordingSftpSyncJobRepository();
        var service = new SftpSyncJobService(repository);
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
        var repository = new RecordingSftpSyncJobRepository();
        var service = new SftpSyncJobService(repository);
        var cancellationToken = new CancellationTokenSource().Token;
        var job = new UpsertSftpSyncJob
        {
            ConnectionProfileId = Guid.NewGuid(),
            Name = "sync incoming",
            SourcePath = "/incoming",
            DestinationPath = "/local/incoming"
        };

        var result = await service.UpsertAsync(job, cancellationToken);

        Assert.Same(repository.UpsertedJob, job);
        Assert.Same(repository.UpsertResult, result);
        Assert.Equal(cancellationToken, repository.LastCancellationToken);
    }

    private sealed class RecordingSftpSyncJobRepository : ISftpSyncJobRepository
    {
        public IReadOnlyCollection<SftpSyncJob> Jobs { get; } =
        [
            new SftpSyncJob
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

        public SftpSyncJob JobById { get; } = new()
        {
            Id = Guid.NewGuid(),
            ConnectionProfileId = Guid.NewGuid(),
            Name = "sync archive",
            SourcePath = "/archive",
            DestinationPath = "/local/archive",
            PollingIntervalSeconds = 300,
            IsEnabled = false
        };

        public SftpSyncJob UpsertResult { get; } = new()
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

        public UpsertSftpSyncJob? UpsertedJob { get; private set; }

        public Task<IReadOnlyCollection<SftpSyncJob>> GetAllAsync(CancellationToken cancellationToken = default)
        {
            LastCancellationToken = cancellationToken;
            return Task.FromResult(Jobs);
        }

        public Task<SftpSyncJob?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
        {
            LastRequestedId = id;
            LastCancellationToken = cancellationToken;
            return Task.FromResult<SftpSyncJob?>(JobById);
        }

        public Task<SftpSyncJob> UpsertAsync(
            UpsertSftpSyncJob job,
            CancellationToken cancellationToken = default)
        {
            UpsertedJob = job;
            LastCancellationToken = cancellationToken;
            return Task.FromResult(UpsertResult);
        }
    }
}
