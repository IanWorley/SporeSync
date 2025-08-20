using Microsoft.AspNetCore.SignalR;

namespace SporeSync.API.Hubs;

public class DownloadStatusHub : Hub
{
    public async Task DownloadStatus(string remotePath, string localPath, CancellationToken cancellationToken)
    {
        var trackedFiles = await _fileTrackingService.ScanDirectoriesAsync(remotePath, cancellationToken);

        foreach (var file in trackedFiles)
        {
            await Clients.All.SendAsync("DownloadProgress", file.Name, file.Progress);
        }


    }


}
