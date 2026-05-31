using Microsoft.AspNetCore.Mvc;
using SporeSync.Business.Interface;
using SporeSync.Domain.Model;
using SporeSync.Infrastructure.Logging;
using SporeSync.Web.DTO;

namespace SporeSync.Web.Controllers;

[ApiController]
[Route("api/system-properties")]
public sealed class SystemPropertiesController : ControllerBase
{
    private readonly ISystemPropertyService _systemPropertyService;
    private readonly DbLoggingConfiguration _dbLoggingConfig;

    public SystemPropertiesController(
        ISystemPropertyService systemPropertyService,
        DbLoggingConfiguration? dbLoggingConfig = null)
    {
        _systemPropertyService = systemPropertyService;
        _dbLoggingConfig = dbLoggingConfig ?? new DbLoggingConfiguration();
    }

    [HttpGet("{propertyName}")]
    public async Task<ActionResult<SystemPropertyResponse>> GetByName(
        string propertyName,
        CancellationToken cancellationToken)
    {
        if (!IsEditableProperty(propertyName))
        {
            return NotFound();
        }

        var systemProperty = await _systemPropertyService.GetByNameAsync(propertyName, cancellationToken);
        if (systemProperty is null)
        {
            return NotFound();
        }

        return Ok(ToResponse(systemProperty));
    }

    [HttpPut("{propertyName}")]
    public async Task<ActionResult<SystemPropertyResponse>> Upsert(
        string propertyName,
        UpsertSystemPropertyRequest request,
        CancellationToken cancellationToken)
    {
        if (!IsEditableProperty(propertyName))
        {
            return NotFound();
        }

        var systemProperty = await _systemPropertyService.UpsertAsync(
            propertyName,
            request.PropertyValue,
            cancellationToken);

        if (string.Equals(propertyName, "db_log_level", StringComparison.OrdinalIgnoreCase))
        {
            _dbLoggingConfig.SetLevel(request.PropertyValue);
        }

        return Ok(ToResponse(systemProperty));
    }

    private static SystemPropertyResponse ToResponse(SystemProperty systemProperty)
    {
        return new SystemPropertyResponse(
            systemProperty.Id,
            systemProperty.PropertyName,
            systemProperty.PropertyValue);
    }

    private static bool IsEditableProperty(string propertyName)
    {
        return string.Equals(propertyName, "db_log_level", StringComparison.OrdinalIgnoreCase);
    }
}
