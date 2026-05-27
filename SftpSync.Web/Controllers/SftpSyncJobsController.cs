using Microsoft.AspNetCore.Mvc;
using SftpSync.Business.Interface;
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
    public ActionResult<IReadOnlyCollection<SftpSyncJobResponse>> GetJobs()
    {
        var jobs = _sftpSyncJobService
            .GetConfiguredJobs()
            .Select(job => new SftpSyncJobResponse(
                job.Id,
                job.Name,
                job.SourcePath,
                job.DestinationPath,
                job.IsEnabled))
            .ToArray();

        return Ok(jobs);
    }
}
