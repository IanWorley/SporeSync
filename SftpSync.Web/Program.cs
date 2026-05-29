using System.Text.Json;
using FluentMigrator.Runner;
using Scalar.AspNetCore;
using SftpSync.Business;
using SftpSync.Business.Interface;
using SftpSync.Infrastructure;
using SftpSync.Web;
using SftpSync.Web.Development;
using SftpSync.Web.Hubs;

var builder = WebApplication.CreateBuilder(args);

var testcontainerDatabase = await TestcontainerDatabase.StartIfEnabledAsync(builder.Configuration);

builder.Services.AddControllers().AddJsonOptions(options => options.JsonSerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase);
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddOpenApi();
builder.Services.AddSignalR();
builder.Services.AddSingleton<IDashboardBroadcaster, DashboardBroadcaster>();
builder.Services.AddSingleton<DevelopmentSimulationService>();
builder.Services.AddHostedService(provider => provider.GetRequiredService<DevelopmentSimulationService>());

builder.Services.RegisterBusinessLogic();
builder.Services.RegisterInfrastructure(builder.Configuration);

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    scope.ServiceProvider.GetRequiredService<IMigrationRunner>().MigrateUp();
    await scope.ServiceProvider.GetRequiredService<IEncryptionKeyInitializer>().InitializeAsync();
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
