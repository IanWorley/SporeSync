using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using SporeSync.Web.Auth;
using SporeSync.Web.DTO;

namespace SporeSync.Web.Controllers;

[ApiController]
[Route("api/auth")]
[AllowAnonymous]
public sealed class AuthController : ControllerBase
{
    public const string LoginRateLimitPolicy = "auth-login";

    private readonly AuthOptions _options;
    private readonly AdminCredentialValidator _credentialValidator;
    private readonly ILogger<AuthController> _logger;

    public AuthController(
        AuthOptions options,
        AdminCredentialValidator credentialValidator,
        ILogger<AuthController> logger)
    {
        _options = options;
        _credentialValidator = credentialValidator;
        _logger = logger;
    }

    [HttpGet("session")]
    public ActionResult<AuthSessionResponse> GetSession()
    {
        return Ok(CurrentSession());
    }

    [HttpPost("login")]
    [EnableRateLimiting(LoginRateLimitPolicy)]
    public async Task<ActionResult<AuthSessionResponse>> Login(LoginRequest request)
    {
        if (!_options.Enabled)
        {
            return Ok(CurrentSession());
        }

        if (!_credentialValidator.Validate(request.Username, request.Password))
        {
            _logger.LogWarning("Failed admin login attempt from {RemoteIp}.", HttpContext.Connection.RemoteIpAddress);
            return Unauthorized(new { message = "Invalid username or password." });
        }

        var identity = new ClaimsIdentity(
            [new Claim(ClaimTypes.Name, _options.Username)],
            CookieAuthenticationDefaults.AuthenticationScheme);

        await HttpContext.SignInAsync(
            CookieAuthenticationDefaults.AuthenticationScheme,
            new ClaimsPrincipal(identity));

        _logger.LogInformation("Admin user {Username} logged in.", _options.Username);
        return Ok(new AuthSessionResponse(AuthRequired: true, Authenticated: true, Username: _options.Username));
    }

    [HttpPost("logout")]
    public async Task<ActionResult<AuthSessionResponse>> Logout()
    {
        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        return Ok(new AuthSessionResponse(AuthRequired: _options.Enabled, Authenticated: false, Username: null));
    }

    private AuthSessionResponse CurrentSession()
    {
        var authenticated = User.Identity?.IsAuthenticated == true;
        return new AuthSessionResponse(
            AuthRequired: _options.Enabled,
            Authenticated: !_options.Enabled || authenticated,
            Username: authenticated ? User.Identity?.Name : null);
    }
}
