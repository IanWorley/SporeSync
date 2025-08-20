using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SporeSync.Domain.Interfaces;
using SporeSync.Domain.Models;
using SporeSync.Infrastructure.Configuration;
using System.IO;
using System.Linq;
using Renci.SshNet;

namespace SporeSync.Infrastructure.Services;

public class RemotePathMonitorService : BackgroundService
{
    private readonly ILogger<RemotePathMonitorService> _logger;
    private readonly IServiceProvider _serviceProvider;
    private readonly IQueueService _queueService;
    private readonly ConcurrentDictionary<string, RemotePathConfig> _monitoredPaths;
    private readonly ConcurrentDictionary<string, Dictionary<string, DateTime>> _pathCache;
    private readonly RemoteMonitorOptions _options;
    private readonly SshClientService _sshClient;
    public RemotePathMonitorService(
        ILogger<RemotePathMonitorService> logger,
        IServiceProvider serviceProvider,
        IQueueService queueService,
        RemoteMonitorOptions options,
        SshClientService sshClient)
    {
        _logger = logger;
        _serviceProvider = serviceProvider;
        _queueService = queueService;
        _options = options;
        _monitoredPaths = new ConcurrentDictionary<string, RemotePathConfig>();
        _pathCache = new ConcurrentDictionary<string, Dictionary<string, DateTime>>();
        _sshClient = sshClient;
    }


    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Remote Path Monitor Service started");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(_options.CheckIntervalSeconds * 1000, stoppingToken);
                var files = await _sshClient.ListFilesAsync(_options.RemotePath);
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
