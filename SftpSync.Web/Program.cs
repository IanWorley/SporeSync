using FluentMigrator.Runner;
using Scalar.AspNetCore;
using SftpSync.Business;
using SftpSync.Infrastructure;
using SftpSync.Web;
using SftpSync.Web.Development;
using SftpSync.Web.Hubs;

var builder = WebApplication.CreateBuilder(args);

var testcontainerDatabase = await TestcontainerDatabase.StartIfEnabledAsync(builder.Configuration);

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
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
}

if (app.Environment.IsDevelopment())
{
    app.MapSwagger("/openapi/{documentName}.json");
    app.MapScalarApiReference(options => options.WithTitle("SftpSync API"));
}

app.UseHttpsRedirection();

app.MapControllers();
app.MapHub<DashboardHub>("/hubs/dashboard");

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
