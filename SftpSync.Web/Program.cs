using System.Text.Json;
using FluentMigrator.Runner;
using Scalar.AspNetCore;
using SftpSync.Business;
using SftpSync.Business.Interface;
using SftpSync.Domain.Interface;
using SftpSync.Infrastructure;
using SftpSync.Infrastructure.Logging;
using SftpSync.Web;
using SftpSync.Web.Hubs;

var builder = WebApplication.CreateBuilder(args);

var testcontainerDatabase = await TestcontainerDatabase.StartIfEnabledAsync(builder.Configuration);

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
    app.MapScalarApiReference(options => options.WithTitle("SftpSync API"));
}

app.UseHttpsRedirection();
app.UseDefaultFiles();
app.UseStaticFiles();

app.MapControllers();
app.MapHub<DashboardHub>("/hubs/dashboard");
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
