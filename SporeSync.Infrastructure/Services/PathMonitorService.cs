using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SporeSync.Application;
using SporeSync.Domain.Interfaces;
using SporeSync.Domain.Models;
using SporeSync.Infrastructure.Configuration;

namespace SporeSync.Infrastructure.Services;

public class PathMonitorService(
    ILogger<PathMonitorService> logger,
    ISshService sshService,
    IOptions<SettingsOptions> config,
    ItemRegistry itemRegistry
    ) : BackgroundService
{
    private readonly ILogger<PathMonitorService> _logger = logger;

    private readonly ISshService _sshService = sshService;

    private readonly SettingsOptions _config = config.Value;

    private readonly ItemRegistry _itemRegistry = itemRegistry;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Remote Path Monitor Service started");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(_config.MonitorOptions.CheckIntervalSeconds * 1000, stoppingToken);
                var files = await _sshService.ListFilesAsync(_config.PathOptions.RemotePath);

                foreach (var file in files)
                {

                    var item = new TrackedItem
                    {
                        FileName = file.Name,
                        FileSize = file.Size,
                        LastModified = file.LastModified,
                        RemotePath = file.Path,
                        IsDirectory = file.IsDirectory,
                        CreatedAt = DateTime.UtcNow,
                        LastSynced = null,
                    };

                    if (file.IsDirectory)
                    {
                        item.Children = await GetRecursiveChildrenAsync(file.Path, stoppingToken);
                    }

                    _logger.LogInformation("Adding item to registry: {Item}, with {Children} children", item, item.Children.Count);
                    _itemRegistry.Add(item);


                }


            }
            catch (OperationCanceledException)
            {
                _logger.LogInformation("Operation canceled, stopping remote path monitoring loop");
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in remote path monitoring loop");
                await Task.Delay(_config.MonitorOptions.ErrorRetryDelaySeconds * 1000, stoppingToken);
            }
        }

        _logger.LogInformation("Remote Path Monitor Service stopped");
    }

    private async Task<List<TrackedItem>> GetRecursiveChildrenAsync(string directoryPath, CancellationToken stoppingToken)
    {
        var children = new List<TrackedItem>();

        try
        {
            var files = await _sshService.ListFilesAsync(directoryPath);

            foreach (var file in files)
            {
                var childItem = new TrackedItem
                {
                    FileName = file.Name,
                    FileSize = file.Size,
                    LastModified = file.LastModified,
                    RemotePath = file.Path,
                    IsDirectory = file.IsDirectory,
                    CreatedAt = DateTime.UtcNow,
                    LastSynced = null,
                };

                if (file.IsDirectory)
                {
                    // Recursively get children of this directory
                    childItem.Children = await GetRecursiveChildrenAsync(file.Path, stoppingToken);
                }

                children.Add(childItem);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting recursive children for directory: {DirectoryPath}", directoryPath);
        }

        return children;
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Stopping Remote Path Monitor Service...");
        await base.StopAsync(cancellationToken);
    }
}
