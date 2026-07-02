using Microsoft.AspNetCore.Mvc;
using SporeSync.Business.Interface;
using SporeSync.Domain.Model;
using SporeSync.Web.DTO;

namespace SporeSync.Web.Controllers;

[ApiController]
[Route("api/sftp-sync-runs")]
public sealed class SporeSyncRunsController : ControllerBase
{
    private readonly ISporeSyncRunService _runService;
    private readonly ISyncRunControlService _runControlService;
    private readonly IDownloadQueueItemService _queueItemService;
    private readonly IDownloadQueueItemFileDeleteService _queueItemFileDeleteService;
    private readonly ISyncDashboardNotifier _dashboardNotifier;

    public SporeSyncRunsController(
        ISporeSyncRunService runService,
        ISyncRunControlService runControlService,
        IDownloadQueueItemService queueItemService,
        IDownloadQueueItemFileDeleteService queueItemFileDeleteService,
        ISyncDashboardNotifier dashboardNotifier)
    {
        _runService = runService;
        _runControlService = runControlService;
        _queueItemService = queueItemService;
        _queueItemFileDeleteService = queueItemFileDeleteService;
        _dashboardNotifier = dashboardNotifier;
    }

    [HttpGet]
    public async Task<ActionResult<PagedResponse<SporeSyncRunResponse>>> GetRuns(
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
    public async Task<ActionResult<SporeSyncRunResponse>> GetById(
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

    [HttpPost("{runId:guid}/queue-items/{queueItemId:guid}/retry")]
    public async Task<ActionResult<DownloadQueueItemResponse>> RetryQueueItem(
        Guid runId,
        Guid queueItemId,
        CancellationToken cancellationToken = default)
    {
        var result = await _queueItemService.RetryAsync(runId, queueItemId, cancellationToken);
        switch (result.Status)
        {
            case RetryQueueItemStatus.NotFound:
                return NotFound();
            case RetryQueueItemStatus.NotRetryable:
                return Conflict(new { message = "Only failed queue items can be retried." });
            default:
                await _dashboardNotifier.NotifyQueueItemUpdatedAsync(result.Item!, cancellationToken);
                return Ok(ToQueueItemResponse(result.Item!));
        }
    }

    [HttpPost("{id:guid}/cancel")]
    public async Task<ActionResult<SporeSyncRunResponse>> Cancel(
        Guid id,
        CancellationToken cancellationToken)
    {
        var result = await _runControlService.CancelRunAsync(id, cancellationToken);
        return result.Error switch
        {
            SyncRunControlError.NotFound => NotFound(),
            SyncRunControlError.NotActive => Problem(
                statusCode: StatusCodes.Status409Conflict,
                title: "Run is not active.",
                detail: "Only queued, scanning, or downloading runs can be cancelled."),
            _ => Ok(ToRunResponse(result.Run!))
        };
    }

    [HttpPost("{id:guid}/retry-failed")]
    public async Task<ActionResult<RetryFailedItemsResponse>> RetryFailed(
        Guid id,
        CancellationToken cancellationToken)
    {
        var result = await _runControlService.RetryFailedItemsAsync(id, cancellationToken);
        return result.Error switch
        {
            SyncRunControlError.NotFound => NotFound(),
            SyncRunControlError.NoFailedItems => Problem(
                statusCode: StatusCodes.Status400BadRequest,
                title: "No failed items.",
                detail: "The run has no failed items to retry."),
            _ => Ok(new RetryFailedItemsResponse(result.RetriedCount, ToRunResponse(result.Run!)))
        };
    }

    [HttpDelete("{runId:guid}/queue-items/{queueItemId:guid}/local")]
    public async Task<ActionResult<DeleteQueueItemFileResponse>> DeleteQueueItemLocalFile(
        Guid runId,
        Guid queueItemId,
        CancellationToken cancellationToken = default)
    {
        var result = await _queueItemFileDeleteService.DeleteLocalAsync(runId, queueItemId, cancellationToken);
        return ToDeleteResponse(result);
    }

    [HttpDelete("{runId:guid}/queue-items/{queueItemId:guid}/remote")]
    public async Task<ActionResult<DeleteQueueItemFileResponse>> DeleteQueueItemRemoteFile(
        Guid runId,
        Guid queueItemId,
        CancellationToken cancellationToken = default)
    {
        var result = await _queueItemFileDeleteService.DeleteRemoteAsync(runId, queueItemId, cancellationToken);
        return ToDeleteResponse(result);
    }

    private ActionResult<DeleteQueueItemFileResponse> ToDeleteResponse(DeleteQueueItemFileResult result)
    {
        return result.Status switch
        {
            DeleteQueueItemFileStatus.NotFound => NotFound(),
            DeleteQueueItemFileStatus.JobNotFound => Problem(
                statusCode: StatusCodes.Status404NotFound,
                title: "Sync job not found.",
                detail: "Queue item's sync job was not found."),
            DeleteQueueItemFileStatus.Failed => Problem(
                statusCode: StatusCodes.Status400BadRequest,
                title: "Delete failed.",
                detail: result.ErrorMessage),
            _ => Ok(new DeleteQueueItemFileResponse(
                result.QueueItemId,
                result.Target,
                result.Path,
                result.Existed))
        };
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

    private static SporeSyncRunResponse ToRunResponse(SporeSyncRun run)
    {
        return new SporeSyncRunResponse(
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
            item.UpdatedAt,
            item.IsGroup,
            item.GroupRemotePath,
            item.ChildCount);
    }
}
