using Microsoft.AspNetCore.Mvc;
using SftpSync.Business.Interface;
using SftpSync.Domain.Model;
using SftpSync.Web.DTO;

namespace SftpSync.Web.Controllers;

[ApiController]
[Route("api/sftp-connection-profiles")]
public sealed class SftpConnectionProfilesController : ControllerBase
{
    private readonly ISftpConnectionProfileService _profileService;

    public SftpConnectionProfilesController(ISftpConnectionProfileService profileService)
    {
        _profileService = profileService;
    }

    [HttpGet]
    public async Task<ActionResult<IReadOnlyCollection<SftpConnectionProfileResponse>>> GetProfiles(
        CancellationToken cancellationToken)
    {
        var profiles = (await _profileService.GetAllAsync(cancellationToken))
            .Select(ToResponse)
            .ToArray();

        return Ok(profiles);
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<SftpConnectionProfileResponse>> GetById(
        Guid id,
        CancellationToken cancellationToken)
    {
        var profile = await _profileService.GetByIdAsync(id, cancellationToken);
        if (profile is null)
        {
            return NotFound();
        }

        return Ok(ToResponse(profile));
    }

    [HttpPost]
    public async Task<ActionResult<SftpConnectionProfileResponse>> Create(
        UpsertSftpConnectionProfileRequest request,
        CancellationToken cancellationToken)
    {
        var profile = await _profileService.UpsertAsync(ToUpsertModel(null, request), cancellationToken);

        return CreatedAtAction(nameof(GetById), new { id = profile.Id }, ToResponse(profile));
    }

    [HttpPut("{id:guid}")]
    public async Task<ActionResult<SftpConnectionProfileResponse>> Update(
        Guid id,
        UpsertSftpConnectionProfileRequest request,
        CancellationToken cancellationToken)
    {
        var profile = await _profileService.UpsertAsync(ToUpsertModel(id, request), cancellationToken);

        return Ok(ToResponse(profile));
    }

    private static UpsertSftpConnectionProfile ToUpsertModel(
        Guid? id,
        UpsertSftpConnectionProfileRequest request)
    {
        return new UpsertSftpConnectionProfile
        {
            Id = id,
            Name = request.Name,
            Host = request.Host,
            Port = request.Port,
            Username = request.Username,
            Password = request.Password,
            PrivateKey = request.PrivateKey,
            PrivateKeyPassphrase = request.PrivateKeyPassphrase,
            IsDefault = request.IsDefault
        };
    }

    private static SftpConnectionProfileResponse ToResponse(SftpConnectionProfile profile)
    {
        return new SftpConnectionProfileResponse(
            profile.Id,
            profile.Name,
            profile.Host,
            profile.Port,
            profile.Username,
            profile.EncryptedPassword is not null,
            profile.EncryptedPrivateKey is not null,
            profile.EncryptedPrivateKeyPassphrase is not null,
            profile.IsDefault);
    }
}
