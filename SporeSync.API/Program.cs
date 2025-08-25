using Scalar.AspNetCore;
using SporeSync.API.Hubs;
using SporeSync.Application.Services;
using SporeSync.Domain.Interfaces;
using SporeSync.Domain.Models;
using SporeSync.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

// Add SSH configuration from separate JSON file
builder.Configuration.AddJsonFile("ssh-config.json", optional: false, reloadOnChange: true);

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


// Add Infrastructure services (SSH, monitoring, etc.)
builder.Services.AddInfrastructure(builder.Configuration);

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
