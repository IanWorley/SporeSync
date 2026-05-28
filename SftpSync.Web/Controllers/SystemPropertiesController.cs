using Microsoft.AspNetCore.Mvc;
using SftpSync.Business.Interface;
using SftpSync.Domain.Model;
using SftpSync.Web.DTO;

namespace SftpSync.Web.Controllers;

[ApiController]
[Route("api/system-properties")]
public sealed class SystemPropertiesController : ControllerBase
{
    private readonly ISystemPropertyService _systemPropertyService;

    public SystemPropertiesController(ISystemPropertyService systemPropertyService)
    {
        _systemPropertyService = systemPropertyService;
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
        return false;
    }
}
