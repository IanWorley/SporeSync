using Scalar.AspNetCore;
using SporeSync.API.Hubs;
using SporeSync.Application;
using SporeSync.Domain;
using SporeSync.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

// Add SSH configuration from separate JSON file (non-sensitive settings only)
builder.Configuration.AddJsonFile("ssh-config.json", optional: true, reloadOnChange: true);

builder.Configuration.AddUserSecrets<Program>();

// Add services to the container.
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddOpenApi();

// Add SignalR for real-time updates
builder.Services.AddSignalR();

// Add CORS for frontend connectivity
builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        policy.AllowAnyOrigin()
              .AllowAnyMethod()
              .AllowAnyHeader();
    });
});

// Add Domain services (interfaces and models)
builder.Services.AddDomain();

// Add Infrastructure services (SSH, monitoring, etc.)
builder.Services.AddInfrastructure(builder.Configuration);

// Add Application services (sync logic, etc.)
builder.Services.AddApplication();


var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.MapScalarApiReference(options =>
    {
        options.Title = "SporeSync API";
        options.Theme = ScalarTheme.Purple;
    });
}

app.UseHttpsRedirection();
app.UseCors();

// Map controllers and SignalR hub
app.MapControllers();
app.MapHub<DownloadStatusHub>("/hubs/downloadstatus");

app.Run();

record WeatherForecast(DateOnly Date, int TemperatureC, string? Summary)
{
    public int TemperatureF => 32 + (int)(TemperatureC / 0.5556);
}
