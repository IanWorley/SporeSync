using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using SporeSync.Application.Services;
using SporeSync.Domain.Models;

namespace SporeSync.API.Controllers;

[ApiController]
[Route("api/[controller]")]
public class SyncController : ControllerBase
{
    private readonly SyncService _syncService;
    private readonly RemotePathMonitorService _monitorService;
    private readonly SshConfiguration _sshConfig;
    private readonly ILogger<SyncController> _logger;

    public SyncController(
        SyncService syncService,
        RemotePathMonitorService monitorService,
        IOptions<SshConfiguration> sshConfig,
        ILogger<SyncController> logger)
    {
        _syncService = syncService;
        _monitorService = monitorService;
        _sshConfig = sshConfig.Value;
        _logger = logger;
    }

    [HttpPost("start-monitoring")]
    public IActionResult StartMonitoring([FromBody] StartMonitoringRequest request)
    {
        try
        {
            var config = new RemotePathConfig
            {
                RemotePath = request.RemotePath,
                LocalPath = request.LocalPath,
                SshConfiguration = _sshConfig,
                CheckIntervalSeconds = request.CheckIntervalSeconds ?? 30
            };

            _monitorService.AddPathToMonitor(config);

            return Ok(new
            {
                Message = "Path monitoring started",
                RemotePath = request.RemotePath,
                LocalPath = request.LocalPath
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error starting path monitoring");
            return BadRequest(new { Error = ex.Message });
        }
    }

    [HttpPost("stop-monitoring")]
    public IActionResult StopMonitoring([FromBody] StopMonitoringRequest request)
    {
        try
        {
            _monitorService.RemovePathFromMonitor(request.RemotePath, _sshConfig.Host);
            return Ok(new
            {
                Message = "Path monitoring stopped",
                RemotePath = request.RemotePath
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error stopping path monitoring");
            return BadRequest(new { Error = ex.Message });
        }
    }

    [HttpGet("path-contents")]
    public async Task<IActionResult> GetPathContents([FromQuery] string remotePath)
    {
        try
        {
            var contents = await _syncService.GetPathContents(remotePath, _sshConfig);
            return Ok(new
            {
                RemotePath = remotePath,
                Items = contents,
                Count = contents.Count
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting path contents");
            return BadRequest(new { Error = ex.Message });
        }
    }

    [HttpPost("sync-folder")]
    public async Task<IActionResult> SyncFolder([FromBody] SyncFolderRequest request)
    {
        try
        {
            await _syncService.SyncFolderAddToQueue(request.RemotePath, _sshConfig);
            return Ok(new
            {
                Message = "Folder sync queued",
                RemotePath = request.RemotePath
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error syncing folder");
            return BadRequest(new { Error = ex.Message });
        }
    }

    [HttpPost("add-sync")]
    public async Task<IActionResult> AddSync([FromBody] AddSyncRequest request)
    {
        try
        {
            await _syncService.AddSyncToQueue(request.RemotePath, request.LocalPath, _sshConfig, request.Operation);
            return Ok(new
            {
                Message = "Sync operation added to queue",
                RemotePath = request.RemotePath,
                LocalPath = request.LocalPath,
                Operation = request.Operation
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error adding sync to queue");
            return BadRequest(new { Error = ex.Message });
        }
    }
}

public class StartMonitoringRequest
{
    public string RemotePath { get; set; } = string.Empty;
    public string LocalPath { get; set; } = string.Empty;
    public int? CheckIntervalSeconds { get; set; }
}

public class StopMonitoringRequest
{
    public string RemotePath { get; set; } = string.Empty;
}

public class SyncFolderRequest
{
    public string RemotePath { get; set; } = string.Empty;
}

public class AddSyncRequest
{
    public string RemotePath { get; set; } = string.Empty;
    public string LocalPath { get; set; } = string.Empty;
    public SyncOperation Operation { get; set; }
}
