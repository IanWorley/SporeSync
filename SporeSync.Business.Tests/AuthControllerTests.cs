using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using SporeSync.Web.Auth;
using SporeSync.Web.Controllers;
using SporeSync.Web.DTO;

namespace SporeSync.Business.Tests;

public sealed class AuthControllerTests
{
    [Fact]
    public async Task Login_SignsInAndReturnsSession_ForValidCredentials()
    {
        var authenticationService = new FakeAuthenticationService();
        var controller = CreateController(
            new AuthOptions { Enabled = true, Username = "admin", Password = "s3cret" },
            authenticationService);

        var result = await controller.Login(new LoginRequest("admin", "s3cret"));

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var session = Assert.IsType<AuthSessionResponse>(ok.Value);
        Assert.True(session.AuthRequired);
        Assert.True(session.Authenticated);
        Assert.Equal("admin", session.Username);
        Assert.NotNull(authenticationService.SignedInPrincipal);
        Assert.Equal("admin", authenticationService.SignedInPrincipal!.Identity?.Name);
    }

    [Fact]
    public async Task Login_ReturnsUnauthorized_ForInvalidCredentials()
    {
        var authenticationService = new FakeAuthenticationService();
        var controller = CreateController(
            new AuthOptions { Enabled = true, Username = "admin", Password = "s3cret" },
            authenticationService);

        var result = await controller.Login(new LoginRequest("admin", "wrong"));

        Assert.IsType<UnauthorizedObjectResult>(result.Result);
        Assert.Null(authenticationService.SignedInPrincipal);
    }

    [Fact]
    public async Task Login_DoesNotSignIn_WhenAuthDisabled()
    {
        var authenticationService = new FakeAuthenticationService();
        var controller = CreateController(new AuthOptions { Enabled = false }, authenticationService);

        var result = await controller.Login(new LoginRequest("anyone", "anything"));

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var session = Assert.IsType<AuthSessionResponse>(ok.Value);
        Assert.False(session.AuthRequired);
        Assert.True(session.Authenticated);
        Assert.Null(authenticationService.SignedInPrincipal);
    }

    [Fact]
    public void GetSession_ReportsUnauthenticated_WhenAuthEnabledAndAnonymous()
    {
        var controller = CreateController(
            new AuthOptions { Enabled = true, Username = "admin", Password = "s3cret" },
            new FakeAuthenticationService());

        var result = controller.GetSession();

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var session = Assert.IsType<AuthSessionResponse>(ok.Value);
        Assert.True(session.AuthRequired);
        Assert.False(session.Authenticated);
        Assert.Null(session.Username);
    }

    [Fact]
    public void GetSession_ReportsAuthenticated_ForSignedInUser()
    {
        var controller = CreateController(
            new AuthOptions { Enabled = true, Username = "admin", Password = "s3cret" },
            new FakeAuthenticationService());
        controller.ControllerContext.HttpContext.User = new ClaimsPrincipal(
            new ClaimsIdentity([new Claim(ClaimTypes.Name, "admin")], "Cookies"));

        var result = controller.GetSession();

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var session = Assert.IsType<AuthSessionResponse>(ok.Value);
        Assert.True(session.AuthRequired);
        Assert.True(session.Authenticated);
        Assert.Equal("admin", session.Username);
    }

    [Fact]
    public void GetSession_ReportsNoAuthRequired_WhenDisabled()
    {
        var controller = CreateController(new AuthOptions { Enabled = false }, new FakeAuthenticationService());

        var result = controller.GetSession();

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var session = Assert.IsType<AuthSessionResponse>(ok.Value);
        Assert.False(session.AuthRequired);
        Assert.True(session.Authenticated);
    }

    [Fact]
    public async Task Logout_SignsOut()
    {
        var authenticationService = new FakeAuthenticationService();
        var controller = CreateController(
            new AuthOptions { Enabled = true, Username = "admin", Password = "s3cret" },
            authenticationService);

        var result = await controller.Logout();

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var session = Assert.IsType<AuthSessionResponse>(ok.Value);
        Assert.True(authenticationService.SignedOut);
        Assert.True(session.AuthRequired);
        Assert.False(session.Authenticated);
    }

    private static AuthController CreateController(
        AuthOptions options,
        FakeAuthenticationService authenticationService)
    {
        var services = new ServiceCollection()
            .AddSingleton<IAuthenticationService>(authenticationService)
            .BuildServiceProvider();

        return new AuthController(
            options,
            new AdminCredentialValidator(options),
            NullLogger<AuthController>.Instance)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext { RequestServices = services }
            }
        };
    }

    private sealed class FakeAuthenticationService : IAuthenticationService
    {
        public ClaimsPrincipal? SignedInPrincipal { get; private set; }
        public bool SignedOut { get; private set; }

        public Task<AuthenticateResult> AuthenticateAsync(HttpContext context, string? scheme)
            => Task.FromResult(AuthenticateResult.NoResult());

        public Task ChallengeAsync(HttpContext context, string? scheme, AuthenticationProperties? properties)
            => Task.CompletedTask;

        public Task ForbidAsync(HttpContext context, string? scheme, AuthenticationProperties? properties)
            => Task.CompletedTask;

        public Task SignInAsync(HttpContext context, string? scheme, ClaimsPrincipal principal, AuthenticationProperties? properties)
        {
            SignedInPrincipal = principal;
            return Task.CompletedTask;
        }

        public Task SignOutAsync(HttpContext context, string? scheme, AuthenticationProperties? properties)
        {
            SignedOut = true;
            return Task.CompletedTask;
        }
    }
}
