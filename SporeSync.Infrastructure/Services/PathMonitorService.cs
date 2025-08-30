using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SporeSync.Domain.Interfaces;
using SporeSync.Infrastructure.Configuration;

namespace SporeSync.Infrastructure.Services;

public class PathMonitorService(
    ILogger<PathMonitorService> logger,
    ISshService sshService,
    IOptions<SettingsOptions> config
    ) : BackgroundService
{
    private readonly ILogger<PathMonitorService> _logger = logger;

    private readonly ISshService _sshService = sshService;

    private readonly SettingsOptions _config = config.Value;

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

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Stopping Remote Path Monitor Service...");
        await base.StopAsync(cancellationToken);
    }
}
