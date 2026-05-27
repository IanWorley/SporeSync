using Microsoft.AspNetCore.Mvc;
using SftpSync.Business.Interface;
using SftpSync.Domain.Model;
using SftpSync.Web.DTO;

namespace SftpSync.Web.Controllers;

[ApiController]
[Route("api/sftp-sync-jobs")]
public sealed class SftpSyncJobsController : ControllerBase
{
    private readonly ISftpSyncJobService _sftpSyncJobService;

    public SftpSyncJobsController(ISftpSyncJobService sftpSyncJobService)
    {
        _sftpSyncJobService = sftpSyncJobService;
    }

    [HttpGet]
    public async Task<ActionResult<IReadOnlyCollection<SftpSyncJobResponse>>> GetJobs(
        CancellationToken cancellationToken)
    {
        var jobs = (await _sftpSyncJobService
            .GetConfiguredJobsAsync(cancellationToken))
            .Select(job => new SftpSyncJobResponse(
                job.Id,
                job.ConnectionProfileId,
                job.Name,
                job.SourcePath,
                job.DestinationPath,
                job.PollingIntervalSeconds,
                job.IsEnabled,
                job.LastPolledAt))
            .ToArray();

        return Ok(jobs);
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<SftpSyncJobResponse>> GetById(
        Guid id,
        CancellationToken cancellationToken)
    {
        var job = await _sftpSyncJobService.GetByIdAsync(id, cancellationToken);
        if (job is null)
        {
            return NotFound();
        }

        return Ok(ToResponse(job));
    }

    [HttpPost]
    public async Task<ActionResult<SftpSyncJobResponse>> Create(
        UpsertSftpSyncJobRequest request,
        CancellationToken cancellationToken)
    {
        var job = await _sftpSyncJobService.UpsertAsync(
            ToUpsertModel(null, request),
            cancellationToken);

        return CreatedAtAction(nameof(GetById), new { id = job.Id }, ToResponse(job));
    }

    [HttpPut("{id:guid}")]
    public async Task<ActionResult<SftpSyncJobResponse>> Update(
        Guid id,
        UpsertSftpSyncJobRequest request,
        CancellationToken cancellationToken)
    {
        var job = await _sftpSyncJobService.UpsertAsync(
            ToUpsertModel(id, request),
            cancellationToken);

        return Ok(ToResponse(job));
    }

    private static UpsertSftpSyncJob ToUpsertModel(Guid? id, UpsertSftpSyncJobRequest request)
    {
        return new UpsertSftpSyncJob
        {
            Id = id,
            ConnectionProfileId = request.ConnectionProfileId,
            Name = request.Name,
            SourcePath = request.SourcePath,
            DestinationPath = request.DestinationPath,
            PollingIntervalSeconds = request.PollingIntervalSeconds,
            IsEnabled = request.IsEnabled
        };
    }

    private static SftpSyncJobResponse ToResponse(SftpSyncJob job)
    {
        return new SftpSyncJobResponse(
            job.Id,
            job.ConnectionProfileId,
            job.Name,
            job.SourcePath,
            job.DestinationPath,
            job.PollingIntervalSeconds,
            job.IsEnabled,
            job.LastPolledAt);
    }
}
