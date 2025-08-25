using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using SporeSync.Domain.Models;

namespace SporeSync.API.Controllers;

[ApiController]
[Route("api/[controller]")]
public class SyncController : ControllerBase
{
    private readonly ILogger<SyncController> _logger;

    public SyncController(ILogger<SyncController> logger)
    {
        _logger = logger;
    }

    [HttpGet("status")]
    public IActionResult GetStatus()
    {
        return Ok(new { Message = "Sync service is running", Timestamp = DateTime.UtcNow });
    }
}
