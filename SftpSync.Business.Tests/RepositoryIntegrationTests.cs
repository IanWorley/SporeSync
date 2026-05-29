using SftpSync.Domain.Model;
using SftpSync.Infrastructure.Repository;

namespace SftpSync.Business.Tests;

public sealed class RepositoryIntegrationTests : IClassFixture<RepositoryTestcontainerFixture>
{
    private readonly RepositoryTestcontainerFixture _fixture;

    public RepositoryIntegrationTests(RepositoryTestcontainerFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task SystemPropertyRepository_UpsertAndGetByName_RoundTripsThroughPostgres()
    {
        var repository = new SystemPropertyRepository(_fixture.DataSource);
        var propertyName = $"sync:test:{Guid.NewGuid()}";

        var upserted = await repository.UpsertAsync(propertyName, "enabled");
        var fetched = await repository.GetByNameAsync(propertyName);

        Assert.NotNull(fetched);
        Assert.Equal(upserted.Id, fetched.Id);
        Assert.Equal(propertyName, fetched.PropertyName);
        Assert.Equal("enabled", fetched.PropertyValue);
    }

    [Fact]
    public async Task SystemPropertyRepository_InsertIfMissing_DoesNotUpdateExistingValue()
    {
        var repository = new SystemPropertyRepository(_fixture.DataSource);
        var propertyName = $"sync:test:insert:{Guid.NewGuid()}";

        var inserted = await repository.InsertIfMissingAsync(propertyName, "first");
        var existing = await repository.InsertIfMissingAsync(propertyName, "second");

        Assert.Equal(inserted.Id, existing.Id);
        Assert.Equal("first", existing.PropertyValue);
    }

    [Fact]
    public async Task SftpConnectionProfileRepository_UpsertAndGetById_RoundTripsThroughPostgres()
    {
        var repository = new SftpConnectionProfileRepository(_fixture.DataSource);
        var profile = new SftpConnectionProfile
        {
            Id = Guid.NewGuid(),
            Name = $"profile-{Guid.NewGuid():N}",
            Host = "sftp.example.com",
            Port = 22,
            Username = "sync-user",
            EncryptedPassword = "encrypted-password",
            IsDefault = false
        };

        var upserted = await repository.UpsertAsync(profile);
        var fetched = await repository.GetByIdAsync(profile.Id);

        Assert.NotNull(fetched);
        Assert.Equal(upserted.Id, fetched.Id);
        Assert.Equal(profile.Name, fetched.Name);
        Assert.Equal(profile.Host, fetched.Host);
        Assert.Equal(profile.Username, fetched.Username);
        Assert.Equal(profile.EncryptedPassword, fetched.EncryptedPassword);
        Assert.False(fetched.IsDefault);
    }

    [Fact]
    public async Task SftpConnectionProfileRepository_HasAnyEncryptedSecretsAsync_ReturnsWhetherAnySecretsExist()
    {
        var repository = new SftpConnectionProfileRepository(_fixture.DataSource);

        await repository.UpsertAsync(new SftpConnectionProfile
        {
            Id = Guid.NewGuid(),
            Name = $"profile-{Guid.NewGuid():N}",
            Host = "sftp.example.com",
            Port = 22,
            Username = "sync-user",
            EncryptedPassword = "encrypted-password",
            IsDefault = false
        });
        var after = await repository.HasAnyEncryptedSecretsAsync();

        Assert.True(after);
    }

    [Fact]
    public async Task SftpSyncJobRepository_UpsertAndGetById_RoundTripsThroughPostgres()
    {
        var profileRepository = new SftpConnectionProfileRepository(_fixture.DataSource);
        var jobRepository = new SftpSyncJobRepository(_fixture.DataSource);

        var profile = await profileRepository.UpsertAsync(new SftpConnectionProfile
        {
            Id = Guid.NewGuid(),
            Name = $"profile-{Guid.NewGuid():N}",
            Host = "sftp.example.com",
            Port = 22,
            Username = "sync-user",
            EncryptedPassword = "encrypted-password",
            IsDefault = false
        });

        var jobId = Guid.NewGuid();
        var upserted = await jobRepository.UpsertAsync(new UpsertSftpSyncJob
        {
            Id = jobId,
            ConnectionProfileId = profile.Id,
            Name = $"job-{Guid.NewGuid():N}",
            SourcePath = "/incoming",
            DestinationPath = "/local/incoming",
            PollingIntervalSeconds = 120,
            IsEnabled = true
        });

        var fetched = await jobRepository.GetByIdAsync(jobId);

        Assert.NotNull(fetched);
        Assert.Equal(upserted.Id, fetched.Id);
        Assert.Equal(profile.Id, fetched.ConnectionProfileId);
        Assert.Equal(upserted.Name, fetched.Name);
        Assert.Equal("/incoming", fetched.SourcePath);
        Assert.Equal("/local/incoming", fetched.DestinationPath);
        Assert.Equal(120, fetched.PollingIntervalSeconds);
        Assert.True(fetched.IsEnabled);
    }

    [Fact]
    public async Task SftpSyncRunRepository_GetRuns_FiltersSearchesSortsAndPages()
    {
        var profileRepository = new SftpConnectionProfileRepository(_fixture.DataSource);
        var jobRepository = new SftpSyncJobRepository(_fixture.DataSource);
        var runRepository = new SftpSyncRunRepository(_fixture.DataSource);

        var profile = await profileRepository.UpsertAsync(CreateProfile());
        var job = await jobRepository.UpsertAsync(CreateJob(profile.Id));
        var matchingRunId = Guid.NewGuid();
        var nonMatchingRunId = Guid.NewGuid();

        await SeedRunAsync(job.Id, matchingRunId, "downloading", "/incoming/match-file.csv");
        await SeedRunAsync(job.Id, nonMatchingRunId, "completed", "/incoming/other-file.csv");

        var result = await runRepository.GetRunsAsync(new RunQuery
        {
            Statuses = ["downloading"],
            Search = "match-file",
            SortBy = "progress",
            SortDirection = "desc",
            PageNumber = 1,
            PageSize = 10
        });

        var run = Assert.Single(result.Items);
        Assert.Equal(matchingRunId, run.Id);
        Assert.Equal(job.Name, run.JobName);
        Assert.Equal(1, result.TotalCount);
    }

    [Fact]
    public async Task DownloadQueueItemRepository_GetByRunId_FiltersSearchesSortsAndPages()
    {
        var profileRepository = new SftpConnectionProfileRepository(_fixture.DataSource);
        var jobRepository = new SftpSyncJobRepository(_fixture.DataSource);
        var queueRepository = new DownloadQueueItemRepository(_fixture.DataSource);

        var profile = await profileRepository.UpsertAsync(CreateProfile());
        var job = await jobRepository.UpsertAsync(CreateJob(profile.Id));
        var runId = Guid.NewGuid();

        await SeedRunAsync(job.Id, runId, "downloading", "/incoming/zeta.csv");
        await SeedQueueItemAsync(job.Id, runId, "/incoming/alpha.csv", "downloading", 100, 50);
        await SeedQueueItemAsync(job.Id, runId, "/incoming/beta.csv", "queued", 100, 0);

        var result = await queueRepository.GetByRunIdAsync(runId, new QueueItemQuery
        {
            Statuses = ["downloading"],
            Search = "alpha",
            SortBy = "basename",
            SortDirection = "asc",
            PageNumber = 1,
            PageSize = 10
        });

        var item = Assert.Single(result.Items);
        Assert.EndsWith("alpha.csv", item.RemotePath);
        Assert.Equal("downloading", item.Status);
        Assert.Equal(1, result.TotalCount);
    }

    private static SftpConnectionProfile CreateProfile()
    {
        return new SftpConnectionProfile
        {
            Id = Guid.NewGuid(),
            Name = $"profile-{Guid.NewGuid():N}",
            Host = "sftp.example.com",
            Port = 22,
            Username = "sync-user",
            EncryptedPassword = "encrypted-password",
            IsDefault = false
        };
    }

    private static UpsertSftpSyncJob CreateJob(Guid profileId)
    {
        return new UpsertSftpSyncJob
        {
            Id = Guid.NewGuid(),
            ConnectionProfileId = profileId,
            Name = $"job-{Guid.NewGuid():N}",
            SourcePath = "/incoming",
            DestinationPath = "/local/incoming",
            PollingIntervalSeconds = 120,
            IsEnabled = true
        };
    }

    private async Task SeedRunAsync(
        Guid jobId,
        Guid runId,
        string status,
        string remotePath)
    {
        const string sql = """
            INSERT INTO core.sftp_sync_runs (
                id,
                job_id,
                status,
                total_file_count,
                completed_file_count,
                total_bytes,
                downloaded_bytes)
            VALUES (
                @run_id,
                @job_id,
                @status,
                1,
                0,
                100,
                50);

            -- Phase 3 note (plan:343): INSERT omits grouping columns (Phase 1 defaults false/NULL/0).
            -- Rows remain visible non-group (Phase 2 filter + rules.md:170 invariant #4). Group+leaf test data in later phases.
            INSERT INTO core.download_queue_items (
                id,
                job_id,
                sync_run_id,
                remote_path,
                destination_path,
                file_size_bytes,
                status,
                bytes_downloaded)
            VALUES (
                @queue_item_id,
                @job_id,
                @run_id,
                @remote_path,
                @destination_path,
                100,
                'downloading',
                50);
            """;

        await using var connection = await _fixture.DataSource.OpenConnectionAsync();
        await using var command = new Npgsql.NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("run_id", runId);
        command.Parameters.AddWithValue("job_id", jobId);
        command.Parameters.AddWithValue("status", status);
        command.Parameters.AddWithValue("queue_item_id", Guid.NewGuid());
        command.Parameters.AddWithValue("remote_path", remotePath);
        command.Parameters.AddWithValue("destination_path", $"/local{remotePath}");
        await command.ExecuteNonQueryAsync();
    }

    private async Task SeedQueueItemAsync(
        Guid jobId,
        Guid runId,
        string remotePath,
        string status,
        long fileSizeBytes,
        long bytesDownloaded)
    {
        const string sql = """
            INSERT INTO core.download_queue_items (
                id,
                job_id,
                sync_run_id,
                remote_path,
                destination_path,
                file_size_bytes,
                status,
                bytes_downloaded)
            VALUES (
                @queue_item_id,
                @job_id,
                @run_id,
                @remote_path,
                @destination_path,
                @file_size_bytes,
                @status,
                @bytes_downloaded);
            """;

        await using var connection = await _fixture.DataSource.OpenConnectionAsync();
        await using var command = new Npgsql.NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("queue_item_id", Guid.NewGuid());
        command.Parameters.AddWithValue("job_id", jobId);
        command.Parameters.AddWithValue("run_id", runId);
        command.Parameters.AddWithValue("remote_path", remotePath);
        command.Parameters.AddWithValue("destination_path", $"/local{remotePath}");
        command.Parameters.AddWithValue("file_size_bytes", fileSizeBytes);
        command.Parameters.AddWithValue("status", status);
        command.Parameters.AddWithValue("bytes_downloaded", bytesDownloaded);
        await command.ExecuteNonQueryAsync();
    }
}
