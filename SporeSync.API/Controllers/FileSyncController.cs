using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using SporeSync.Domain.Interfaces;
using SporeSync.Domain.Models;

namespace SporeSync.API.Controllers;

[ApiController]
[Route("api/[controller]")]
public class FileSyncController : ControllerBase
{
    private readonly ISshService _sshService;
    private readonly SshConfiguration _config;

    public FileSyncController(ISshService sshService, IOptions<SshConfiguration> config)
    {
        _sshService = sshService;
        _config = config.Value;
    }

    [HttpPost("upload")]
    public async Task<IActionResult> UploadFile([FromBody] UploadFileRequest request)
    {
        try
        {
            var progress = new Progress<UploadProgress>(p =>
            {
                // You can send this progress via SignalR or log it
                Console.WriteLine($"Uploaded {p.ProgressPercentage:F1}% of {p.FileName}");
            });

            var success = await _sshService.UploadFileAsync(
                _config,
                request.LocalPath,
                request.RemotePath,
                progress);

            return Ok(new { Success = success });
        }
        catch (Exception ex)
        {
            return BadRequest(new { Error = ex.Message });
        }
    }

    [HttpGet("files")]
    public async Task<IActionResult> ListFiles([FromQuery] string remotePath = "/")
    {
        try
        {
            var files = await _sshService.ListFilesAsync(_config, remotePath);
            return Ok(files);
        }
        catch (Exception ex)
        {
            return BadRequest(new { Error = ex.Message });
        }
    }

    [HttpPost("test-connection")]
    public async Task<IActionResult> TestConnection()
    {
        try
        {
            var isConnected = await _sshService.TestConnectionAsync(_config);
            return Ok(new { IsConnected = isConnected });
        }
        catch (Exception ex)
        {
            return BadRequest(new { Error = ex.Message });
        }
    }

    [HttpPost("upload-directory")]
    public async Task<IActionResult> UploadDirectory([FromBody] UploadDirectoryRequest request)
    {
        try
        {
            var progress = new Progress<UploadProgress>(p =>
            {
                // You can send this progress via SignalR or log it
                Console.WriteLine($"Directory upload: {p.FileName} - {p.ProgressPercentage:F1}%");
            });

            var success = await _sshService.UploadDirectoryAsync(
                _config,
                request.LocalPath,
                request.RemotePath,
                progress);

            return Ok(new { Success = success });
        }
        catch (Exception ex)
        {
            return BadRequest(new { Error = ex.Message });
        }
    }

    [HttpPost("download-directory")]
    public async Task<IActionResult> DownloadDirectory([FromBody] DownloadDirectoryRequest request)
    {
        try
        {
            var progress = new Progress<UploadProgress>(p =>
            {
                // You can send this progress via SignalR or log it
                Console.WriteLine($"Directory download: {p.FileName} - {p.ProgressPercentage:F1}%");
            });

            var success = await _sshService.DownloadDirectoryAsync(
                _config,
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

public class UploadFileRequest
{
    public string LocalPath { get; set; } = string.Empty;
    public string RemotePath { get; set; } = string.Empty;
}


