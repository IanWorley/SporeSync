using System.Collections.Concurrent;
using System.IO;
using System.Linq;
using System.Threading;

using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Renci.SshNet;
using SporeSync.Domain.Interfaces;
using SporeSync.Infrastructure.Configuration;

namespace SporeSync.Infrastructure.Services;

public class RemotePathMonitorService(
    ILogger<RemotePathMonitorService> logger,
    IServiceProvider serviceProvider,
    IQueueService queueService,
    RemoteMonitorOptions options,
    SshClientService sshClient,
    RemotePathOptions remotePathConfig) : BackgroundService
{
    private readonly ILogger<RemotePathMonitorService> _logger = logger;
    private readonly ConcurrentDictionary<string, RemotePathOptions> _monitoredPaths = new ConcurrentDictionary<string, RemotePathOptions>();
    private readonly ConcurrentDictionary<string, Dictionary<string, DateTime>> _pathCache = new ConcurrentDictionary<string, Dictionary<string, DateTime>>();
    private readonly RemoteMonitorOptions _options = options;

    private readonly RemotePathOptions _remotePathConfig = remotePathConfig;

    private readonly SshClientService _sshClient = sshClient;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Remote Path Monitor Service started");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(_options.CheckIntervalSeconds * 1000, stoppingToken);
                var files = await _sshClient.ListFilesAsync(_remotePathConfig.RemotePath);

                foreach (var file in files)
                {
                    if (file.IsDirectory)
                    {
                        _logger.LogInformation("Found directory: {FileName} with size {FileSize} bytes, last modified {LastModified}",
                            file.Name, file.Size, file.LastModified);
                    }
                    else
                    {
                        _logger.LogInformation("Found file: {FileName} with size {FileSize} bytes, last modified {LastModified}",
                            file.Name, file.Size, file.LastModified);
                    }
                }

            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in remote path monitoring loop");
                await Task.Delay(_options.ErrorRetryDelaySeconds * 1000, stoppingToken);
            }
        }

        _logger.LogInformation("Remote Path Monitor Service stopped");
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Stopping Remote Path Monitor Service...");
        await base.StopAsync(cancellationToken);
    }
}
