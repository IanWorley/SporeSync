using Microsoft.AspNetCore.Mvc;
using SporeSync.Business.Interface;
using SporeSync.Business.Sftp;
using SporeSync.Domain.Model;
using SporeSync.Web.DTO;

namespace SporeSync.Web.Controllers;

[ApiController]
[Route("api/sftp-connection-profiles")]
public sealed class SftpConnectionProfilesController : ControllerBase
{
    private readonly ISftpConnectionProfileService _profileService;
    private readonly ISshHostKeyScanner _hostKeyScanner;

    public SftpConnectionProfilesController(
        ISftpConnectionProfileService profileService,
        ISshHostKeyScanner hostKeyScanner)
    {
        _profileService = profileService;
        _hostKeyScanner = hostKeyScanner;
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
        SftpConnectionProfile profile;
        try
        {
            profile = await _profileService.UpsertAsync(ToUpsertModel(null, request), cancellationToken);
        }
        catch (FormatException ex)
        {
            return ValidationProblem(ex.Message);
        }

        return CreatedAtAction(nameof(GetById), new { id = profile.Id }, ToResponse(profile));
    }

    [HttpPut("{id:guid}")]
    public async Task<ActionResult<SftpConnectionProfileResponse>> Update(
        Guid id,
        UpsertSftpConnectionProfileRequest request,
        CancellationToken cancellationToken)
    {
        SftpConnectionProfile profile;
        try
        {
            profile = await _profileService.UpsertAsync(ToUpsertModel(id, request), cancellationToken);
        }
        catch (FormatException ex)
        {
            return ValidationProblem(ex.Message);
        }

        return Ok(ToResponse(profile));
    }

    /// <summary>
    /// Retrieves the SSH host key fingerprint presented by a server without sending any
    /// credentials, so an operator can review and confirm it before pinning.
    /// </summary>
    [HttpPost("host-key-scan")]
    public async Task<ActionResult<HostKeyScanResponse>> ScanHostKey(
        ScanHostKeyRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var result = await _hostKeyScanner.ScanAsync(request.Host, request.Port, cancellationToken);

            return Ok(new HostKeyScanResponse(
                result.HostKeyAlgorithm,
                result.KeyLength,
                result.FingerprintSha256));
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return Problem(
                title: $"Unable to retrieve the host key from {request.Host}:{request.Port}.",
                detail: ex.Message,
                statusCode: StatusCodes.Status502BadGateway);
        }
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
            HostKeyFingerprintSha256 = request.HostKeyFingerprintSha256,
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
            profile.HostKeyFingerprintSha256,
            profile.IsDefault);
    }
}
