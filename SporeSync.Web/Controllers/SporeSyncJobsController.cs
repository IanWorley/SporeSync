using Microsoft.AspNetCore.Mvc;
using SporeSync.Business.Interface;
using SporeSync.Domain.Model;
using SporeSync.Web;
using SporeSync.Web.DTO;

namespace SporeSync.Web.Controllers;

[ApiController]
[Route("api/sftp-sync-jobs")]
public sealed class SporeSyncJobsController : ControllerBase
{
    private readonly ISporeSyncJobService _sporeSyncJobService;
    private readonly ISyncJobRunService _syncJobRunService;

    public SporeSyncJobsController(
        ISporeSyncJobService sporeSyncJobService,
        ISyncJobRunService syncJobRunService)
    {
        _sporeSyncJobService = sporeSyncJobService;
        _syncJobRunService = syncJobRunService;
    }

    [HttpGet]
    public async Task<ActionResult<IReadOnlyCollection<SporeSyncJobResponse>>> GetJobs(
        CancellationToken cancellationToken)
    {
        var jobs = (await _sporeSyncJobService
            .GetConfiguredJobsAsync(cancellationToken))
            .Select(job => new SporeSyncJobResponse(
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
    public async Task<ActionResult<SporeSyncJobResponse>> GetById(
        Guid id,
        CancellationToken cancellationToken)
    {
        var job = await _sporeSyncJobService.GetByIdAsync(id, cancellationToken);
        if (job is null)
        {
            return NotFound();
        }

        return Ok(ToResponse(job));
    }

    [HttpPost]
    public async Task<ActionResult<SporeSyncJobResponse>> Create(
        UpsertSporeSyncJobRequest request,
        CancellationToken cancellationToken)
    {
        SporeSyncJob job;
        try
        {
            job = await _sporeSyncJobService.UpsertAsync(
                ToUpsertModel(null, request),
                cancellationToken);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { message = ex.Message });
        }

        return CreatedAtAction(nameof(GetById), new { id = job.Id }, ToResponse(job));
    }

    [HttpPut("{id:guid}")]
    public async Task<ActionResult<SporeSyncJobResponse>> Update(
        Guid id,
        UpsertSporeSyncJobRequest request,
        CancellationToken cancellationToken)
    {
        SporeSyncJob job;
        try
        {
            job = await _sporeSyncJobService.UpsertAsync(
                ToUpsertModel(id, request),
                cancellationToken);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { message = ex.Message });
        }

        return Ok(ToResponse(job));
    }

    [HttpPost("{id:guid}/run")]
    public async Task<ActionResult<SporeSyncRunResponse>> RunNow(
        Guid id,
        CancellationToken cancellationToken)
    {
        var result = await _syncJobRunService.TriggerManualRunAsync(id, cancellationToken);
        return result.Error switch
        {
            SyncJobRunError.NotFound => NotFound(),
            SyncJobRunError.Disabled => BadRequest(new { message = "Job is disabled." }),
            SyncJobRunError.ActiveRunExists => Conflict(new { message = "Job already has an active run." }),
            _ => Accepted(SyncDashboardNotifier.ToRunResponse(result.Run!))
        };
    }

    private static UpsertSporeSyncJob ToUpsertModel(Guid? id, UpsertSporeSyncJobRequest request)
    {
        return new UpsertSporeSyncJob
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

    private static SporeSyncJobResponse ToResponse(SporeSyncJob job)
    {
        return new SporeSyncJobResponse(
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
