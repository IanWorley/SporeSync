using Microsoft.AspNetCore.SignalR;

namespace SftpSync.Web.Hubs;

public sealed class DashboardHub : Hub
{
    public Task SubscribeDashboard()
    {
        return Groups.AddToGroupAsync(Context.ConnectionId, "dashboard");
    }

    public Task SubscribeRun(Guid runId)
    {
        return Groups.AddToGroupAsync(Context.ConnectionId, GetRunGroupName(runId));
    }

    public Task UnsubscribeRun(Guid runId)
    {
        return Groups.RemoveFromGroupAsync(Context.ConnectionId, GetRunGroupName(runId));
    }

    public static string GetRunGroupName(Guid runId)
    {
        return $"run:{runId}";
    }
}
