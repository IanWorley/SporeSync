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
}
