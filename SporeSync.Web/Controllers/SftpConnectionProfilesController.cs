using System.ComponentModel.DataAnnotations;
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
    private readonly ISftpConnectionTestService _connectionTestService;

    public SftpConnectionProfilesController(
        ISftpConnectionProfileService profileService,
        ISshHostKeyScanner hostKeyScanner,
        ISftpConnectionTestService connectionTestService)
    {
        _profileService = profileService;
        _hostKeyScanner = hostKeyScanner;
        _connectionTestService = connectionTestService;
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
        catch (ValidationException ex)
        {
            return InvalidProfileRequest(ex);
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
        catch (ValidationException ex)
        {
            return InvalidProfileRequest(ex);
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

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(
        Guid id,
        CancellationToken cancellationToken)
    {
        var status = await _profileService.DeleteAsync(id, cancellationToken);
        return status switch
        {
            DeleteSftpConnectionProfileStatus.NotFound => NotFound(),
            DeleteSftpConnectionProfileStatus.InUse => Problem(
                statusCode: StatusCodes.Status409Conflict,
                title: "Profile in use.",
                detail: "One or more sync jobs use this connection profile. Delete or reassign them first."),
            _ => NoContent()
        };
    }

    [HttpPost("test")]
    public async Task<ActionResult<SftpConnectionTestResponse>> Test(
        TestSftpConnectionRequest request,
        CancellationToken cancellationToken)
    {
        SftpConnectionTestResult result;
        try
        {
            result = await _connectionTestService.TestAsync(new SftpConnectionTestRequest
            {
                ProfileId = request.ProfileId,
                Host = request.Host,
                Port = request.Port,
                Username = request.Username,
                AuthenticationMethod = ParseAuthenticationMethod(request.AuthenticationMethod),
                Password = request.Password,
                PrivateKey = request.PrivateKey,
                PrivateKeyPassphrase = request.PrivateKeyPassphrase,
                RemovePrivateKeyPassphrase = request.RemovePrivateKeyPassphrase,
                HostKeyFingerprintSha256 = request.HostKeyFingerprintSha256,
                SourcePath = request.SourcePath
            }, cancellationToken);
        }
        catch (FormatException ex)
        {
            return ValidationProblem(ex.Message);
        }

        if (!result.ProfileFound)
        {
            return NotFound();
        }

        return Ok(new SftpConnectionTestResponse(
            result.Success,
            result.FailureType,
            result.Message,
            result.DurationMs));
    }

    private ActionResult InvalidProfileRequest(ValidationException exception)
    {
        ModelState.AddModelError(string.Empty, exception.Message);
        return ValidationProblem(ModelState);
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
            AuthenticationMethod = ParseAuthenticationMethod(request.AuthenticationMethod),
            Password = request.Password,
            PrivateKey = request.PrivateKey,
            PrivateKeyPassphrase = request.PrivateKeyPassphrase,
            RemovePrivateKeyPassphrase = request.RemovePrivateKeyPassphrase,
            TrustedHostKeyFingerprintsSha256 = request.TrustedHostKeyFingerprintsSha256,
            IsDefault = request.IsDefault
        };
    }

    private static SftpAuthenticationMethod ParseAuthenticationMethod(string value)
    {
        return value switch
        {
            "password" => SftpAuthenticationMethod.Password,
            "privateKey" => SftpAuthenticationMethod.PrivateKey,
            _ => throw new ValidationException(
                "Authentication method must be either 'password' or 'privateKey'.")
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
            profile.EncryptedPrivateKey is not null ? "privateKey" : "password",
            profile.EncryptedPassword is not null,
            profile.EncryptedPrivateKey is not null,
            profile.EncryptedPrivateKeyPassphrase is not null,
            profile.TrustedHostKeyFingerprintsSha256,
            profile.IsDefault);
    }
}
