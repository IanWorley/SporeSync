using System.Text.Json;
using System.Threading.RateLimiting;
using FluentMigrator.Runner;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.RateLimiting;
using Scalar.AspNetCore;
using SporeSync.Business;
using SporeSync.Business.Interface;
using SporeSync.Domain.Interface;
using SporeSync.Infrastructure;
using SporeSync.Infrastructure.Logging;
using SporeSync.Web;
using SporeSync.Web.Auth;
using SporeSync.Web.Controllers;
using SporeSync.Web.Hubs;
using SporeSync.Web.Security;

if (args is ["hash-password", ..])
{
    var password = args.Length > 1 ? args[1] : null;
    if (password is null)
    {
        Console.Write("Password: ");
        password = Console.ReadLine();
    }

    if (string.IsNullOrEmpty(password))
    {
        Console.Error.WriteLine("A non-empty password is required.");
        return 1;
    }

    Console.WriteLine(PasswordHasher.Hash(password));
    return 0;
}

var builder = WebApplication.CreateBuilder(args);

var testcontainerDatabase = await TestcontainerDatabase.StartIfEnabledAsync(builder.Configuration);

var forwardedHeaderSettings =
    builder.Configuration.GetSection(ForwardedHeaderSettings.SectionName).Get<ForwardedHeaderSettings>()
    ?? new ForwardedHeaderSettings();
forwardedHeaderSettings.Validate();

if (forwardedHeaderSettings.Enabled)
{
    builder.Services.Configure<ForwardedHeadersOptions>(forwardedHeaderSettings.Configure);
}

var authOptions = builder.Configuration.GetSection(AuthOptions.SectionName).Get<AuthOptions>() ?? new AuthOptions();
authOptions.Validate();
builder.Services.AddSingleton(authOptions);
builder.Services.AddSingleton<AdminCredentialValidator>();

builder.Services
    .AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.Cookie.Name = ".SporeSync.Auth";
        options.Cookie.HttpOnly = true;
        options.Cookie.SameSite = SameSiteMode.Lax;
        options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
        options.ExpireTimeSpan = TimeSpan.FromHours(authOptions.SessionHours);
        options.SlidingExpiration = true;

        // The SPA handles login navigation, so the API and hub endpoints
        // return status codes instead of redirecting to a login page.
        options.Events.OnRedirectToLogin = context =>
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            return Task.CompletedTask;
        };
        options.Events.OnRedirectToAccessDenied = context =>
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            return Task.CompletedTask;
        };
    });
builder.Services.AddAuthorization();
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddPolicy(AuthController.LoginRateLimitPolicy, context =>
        RateLimitPartition.GetFixedWindowLimiter(
            context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 10,
                Window = TimeSpan.FromMinutes(1)
            }));
});

builder.Services.AddControllers().AddJsonOptions(options => options.JsonSerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase);
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddOpenApi();
builder.Services.AddSignalR();
builder.Services.AddSingleton<IDashboardBroadcaster, DashboardBroadcaster>();
builder.Services.AddSingleton<ISyncDashboardNotifier, SyncDashboardNotifier>();

builder.Services.RegisterBusinessLogic(builder.Configuration);
builder.Services.RegisterInfrastructure(builder.Configuration);

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    DbCommandLogger.Configure(
        scope.ServiceProvider.GetRequiredService<DbLoggingConfiguration>(),
        scope.ServiceProvider.GetRequiredService<DbCallLogBuffer>());

    scope.ServiceProvider.GetRequiredService<IMigrationRunner>().MigrateUp();
    await scope.ServiceProvider.GetRequiredService<IEncryptionKeyInitializer>().InitializeAsync();

    var dbLogConfig = scope.ServiceProvider.GetRequiredService<DbLoggingConfiguration>();
    var propRepo = scope.ServiceProvider.GetRequiredService<ISystemPropertyRepository>();
    var initialLevel = await propRepo.GetByNameAsync("db_log_level");
    dbLogConfig.SetLevel(initialLevel?.PropertyValue);
}

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi("/openapi/{documentName}.json");
    app.MapScalarApiReference(options => options.WithTitle("SporeSync API"));
}

if (forwardedHeaderSettings.Enabled)
{
    app.UseForwardedHeaders();
}

app.UseHttpsRedirection();
app.UseDefaultFiles();
app.UseStaticFiles();

app.UseAuthentication();
app.UseAuthorization();
app.UseRateLimiter();

var controllers = app.MapControllers();
var dashboardHub = app.MapHub<DashboardHub>("/hubs/dashboard");

if (authOptions.Enabled)
{
    // AuthController opts out with [AllowAnonymous] so login and session
    // discovery keep working. Static SPA assets stay anonymous; the SPA
    // itself redirects to /login based on /api/auth/session.
    controllers.RequireAuthorization();
    dashboardHub.RequireAuthorization();
}

app.MapFallbackToFile("index.html");

try
{
    await app.RunAsync();
}
finally
{
    if (testcontainerDatabase is not null)
    {
        await testcontainerDatabase.DisposeAsync();
    }
}

return 0;
