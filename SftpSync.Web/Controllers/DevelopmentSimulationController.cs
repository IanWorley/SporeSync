using Microsoft.AspNetCore.Mvc;
using SftpSync.Web.Development;

namespace SftpSync.Web.Controllers;

[ApiController]
[Route("api/development/simulation")]
public sealed class DevelopmentSimulationController : ControllerBase
{
    private readonly DevelopmentSimulationService _simulationService;
    private readonly IWebHostEnvironment _environment;

    public DevelopmentSimulationController(
        DevelopmentSimulationService simulationService,
        IWebHostEnvironment environment)
    {
        _simulationService = simulationService;
        _environment = environment;
    }

    [HttpPost("seed")]
    public async Task<ActionResult<object>> Seed(CancellationToken cancellationToken)
    {
        if (!_environment.IsDevelopment())
        {
            return NotFound();
        }

        var runId = await _simulationService.SeedAsync(cancellationToken);
        return Ok(new { runId });
    }

    [HttpPost("start")]
    public ActionResult<object> Start()
    {
        if (!_environment.IsDevelopment())
        {
            return NotFound();
        }

        _simulationService.StartSimulation();
        return Ok(new { isRunning = _simulationService.IsRunning });
    }

    [HttpPost("stop")]
    public ActionResult<object> Stop()
    {
        if (!_environment.IsDevelopment())
        {
            return NotFound();
        }

        _simulationService.StopSimulation();
        return Ok(new { isRunning = _simulationService.IsRunning });
    }

    // Phase 6 (plan:374): dev-only requeue for a failed opaque group (and its subtree leaves).
    // Uses the exact semantics defined in Phase 5 + grouping-rules.md:129-134.
    // Enables demonstrating group failure + requeue in the simulation without touching production paths.
    [HttpPost("requeue-group/{queueItemId}")]
    public async Task<ActionResult<object>> RequeueGroup(Guid queueItemId, CancellationToken cancellationToken)
    {
        if (!_environment.IsDevelopment())
        {
            return NotFound();
        }

        await _simulationService.RequeueGroupAsync(queueItemId, cancellationToken);
        return Ok(new { queueItemId, requeued = true, note = "Group + all linked leaves reset to 'queued'. Start simulation to observe re-advance." });
    }
}
