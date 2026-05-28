using Microsoft.AspNetCore.Mvc;
using SftpSync.Business.Interface;
using SftpSync.Domain.Model;
using SftpSync.Web.DTO;

namespace SftpSync.Web.Controllers;

[ApiController]
[Route("api/sftp-sync-runs")]
public sealed class SftpSyncRunsController : ControllerBase
{
    private readonly ISftpSyncRunService _runService;
    private readonly IDownloadQueueItemService _queueItemService;

    public SftpSyncRunsController(
        ISftpSyncRunService runService,
        IDownloadQueueItemService queueItemService)
    {
        _runService = runService;
        _queueItemService = queueItemService;
    }

    [HttpGet]
    public async Task<ActionResult<PagedResponse<SftpSyncRunResponse>>> GetRuns(
        [FromQuery] string[]? status,
        [FromQuery] string? search,
        [FromQuery] string? sortBy,
        [FromQuery] string? sortDirection,
        [FromQuery] int pageNumber = 1,
        [FromQuery] int pageSize = 25,
        CancellationToken cancellationToken = default)
    {
        var result = await _runService.GetRunsAsync(
            new RunQuery
            {
                Statuses = status ?? Array.Empty<string>(),
                Search = search,
                SortBy = sortBy ?? "startedAt",
                SortDirection = sortDirection ?? "desc",
                PageNumber = pageNumber,
                PageSize = pageSize
            },
            cancellationToken);

        return Ok(ToPagedResponse(result, ToRunResponse));
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<SftpSyncRunResponse>> GetById(
        Guid id,
        CancellationToken cancellationToken)
    {
        var run = await _runService.GetByIdAsync(id, cancellationToken);
        if (run is null)
        {
            return NotFound();
        }

        return Ok(ToRunResponse(run));
    }

    [HttpGet("{id:guid}/queue-items")]
    public async Task<ActionResult<PagedResponse<DownloadQueueItemResponse>>> GetQueueItems(
        Guid id,
        [FromQuery] string[]? status,
        [FromQuery] string? search,
        [FromQuery] string? sortBy,
        [FromQuery] string? sortDirection,
        [FromQuery] int pageNumber = 1,
        [FromQuery] int pageSize = 50,
        CancellationToken cancellationToken = default)
    {
        var result = await _queueItemService.GetByRunIdAsync(
            id,
            new QueueItemQuery
            {
                Statuses = status ?? Array.Empty<string>(),
                Search = search,
                SortBy = sortBy ?? "queuedAt",
                SortDirection = sortDirection ?? "desc",
                PageNumber = pageNumber,
                PageSize = pageSize
            },
            cancellationToken);

        return Ok(ToPagedResponse(result, ToQueueItemResponse));
    }

    private static PagedResponse<TResponse> ToPagedResponse<TModel, TResponse>(
        PagedResult<TModel> result,
        Func<TModel, TResponse> map)
    {
        return new PagedResponse<TResponse>(
            result.Items.Select(map).ToArray(),
            result.PageNumber,
            result.PageSize,
            result.TotalCount);
    }

    private static SftpSyncRunResponse ToRunResponse(SftpSyncRun run)
    {
        return new SftpSyncRunResponse(
            run.Id,
            run.JobId,
            run.JobName,
            run.Status,
            run.StartedAt,
            run.CompletedAt,
            run.TotalFileCount,
            run.CompletedFileCount,
            run.SkippedFileCount,
            run.FailedFileCount,
            run.TotalBytes,
            run.DownloadedBytes,
            run.CurrentBytesPerSecond,
            run.ErrorMessage);
    }

    private static DownloadQueueItemResponse ToQueueItemResponse(DownloadQueueItem item)
    {
        return new DownloadQueueItemResponse(
            item.Id,
            item.JobId,
            item.SyncRunId,
            item.RemotePath,
            item.DestinationPath,
            item.FileSizeBytes,
            item.RemoteModifiedAt,
            item.Status,
            item.BytesDownloaded,
            item.CurrentBytesPerSecond,
            item.RetryCount,
            item.HandledReason,
            item.ErrorMessage,
            item.QueuedAt,
            item.StartedAt,
            item.CompletedAt,
            item.UpdatedAt);
    }
}
