using Microsoft.AspNetCore.Mvc;
using SporeSync.Application.Services;
using SporeSync.Domain.Interfaces;
using SporeSync.Domain.Models;

namespace SporeSync.API.Controllers;

[ApiController]
[Route("api/[controller]")]
public class SyncController : ControllerBase
{
    private readonly SyncService _syncService;

    public SyncController(SyncService syncService)
    {
        _syncService = syncService;
    }

    [HttpPost("sync-directory")]
    public async Task<IActionResult> SyncDirectory([FromBody] SyncDirectoryRequest request)
    {
        try
        {
            var progress = new Progress<UploadProgress>(p =>
            {
                // You can send this progress via SignalR or log it
                Console.WriteLine($"Sync progress: {p.FileName} - {p.ProgressPercentage:F1}%");
            });

            var success = await _syncService.SyncDirectoryAsync(
                request.RemotePath,
                request.LocalPath,
                progress);

            return Ok(new { Success = success });
        }
        catch (Exception ex)
        {
            return BadRequest(new { Error = ex.Message });
        }
    }

    [HttpGet("remote-files")]
    public async Task<IActionResult> GetRemoteFiles([FromQuery] string remotePath = "/")
    {
        try
        {
            var files = await _syncService.GetRemoteFilesAsync(remotePath);
            return Ok(files);
        }
        catch (Exception ex)
        {
            return BadRequest(new { Error = ex.Message });
        }
    }



    [HttpPost("sync-file")]
    public async Task<IActionResult> SyncFile([FromBody] SyncFileRequest request)
    {
        try
        {
            var progress = new Progress<UploadProgress>(p =>
            {
                Console.WriteLine($"File sync: {p.FileName} - {p.ProgressPercentage:F1}%");
            });

            var success = await _syncService.SyncSingleFileAsync(
                request.RemotePath,
                request.LocalPath,
                progress);

            return Ok(new { Success = success });
        }
        catch (Exception ex)
        {
            return BadRequest(new { Error = ex.Message });
        }
    }
}

public class SyncDirectoryRequest
{
    public string RemotePath { get; set; } = string.Empty;
    public string LocalPath { get; set; } = string.Empty;
}

public class SyncFileRequest
{
    public string RemotePath { get; set; } = string.Empty;
    public string LocalPath { get; set; } = string.Empty;
}
