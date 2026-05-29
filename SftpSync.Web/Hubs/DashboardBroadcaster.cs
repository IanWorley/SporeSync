using Microsoft.AspNetCore.SignalR;
using SftpSync.Web.DTO;

namespace SftpSync.Web.Hubs;

public sealed class DashboardBroadcaster : IDashboardBroadcaster
{
    private readonly IHubContext<DashboardHub> _hubContext;

    public DashboardBroadcaster(IHubContext<DashboardHub> hubContext)
    {
        _hubContext = hubContext;
    }

    // Phase 5 readiness (plan:364 + grouping-rules.md:129-134 + locked #4):
    // QueueItemUpdated sends the full DownloadQueueItemResponse (incl. Phase 3 IsGroup/GroupRemotePath/ChildCount).
    // When a worker updates a group row (or its leaves, then re-broadcasts the *visible group* with updated aggregates),
    // clients (still fully opaque per Phase 7) receive it via the existing "QueueItemUpdated" event + run-group subscription.
    // No special group handling or leaf→group fan-out is required in the broadcaster itself.
    // See also IDashboardBroadcaster.cs.

    public async Task RunUpdatedAsync(
        SftpSyncRunResponse run,
        CancellationToken cancellationToken = default)
    {
        await _hubContext.Clients.Group("dashboard")
            .SendAsync("RunUpdated", run, cancellationToken);
        await _hubContext.Clients.Group(DashboardHub.GetRunGroupName(run.Id))
            .SendAsync("RunUpdated", run, cancellationToken);
    }

    public Task QueueItemUpdatedAsync(
        DownloadQueueItemResponse item,
        CancellationToken cancellationToken = default)
    {
        var groupName = item.SyncRunId is null
            ? "dashboard"
            : DashboardHub.GetRunGroupName(item.SyncRunId.Value);

        return _hubContext.Clients.Group(groupName)
            .SendAsync("QueueItemUpdated", item, cancellationToken);
    }

    public Task QueueItemRemovedAsync(
        Guid runId,
        Guid queueItemId,
        CancellationToken cancellationToken = default)
    {
        return _hubContext.Clients.Group(DashboardHub.GetRunGroupName(runId))
            .SendAsync("QueueItemRemoved", new { runId, queueItemId }, cancellationToken);
    }

    public Task LogAppendedAsync(object logEntry, CancellationToken cancellationToken = default)
    {
        return _hubContext.Clients.Group("dashboard")
            .SendAsync("LogAppended", logEntry, cancellationToken);
    }
}
