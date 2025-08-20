using SporeSync.API.Hubs;
using SporeSync.Application.Services;
using SporeSync.Domain.Interfaces;
using SporeSync.Domain.Models;
using Scalar.AspNetCore;

var builder = WebApplication.CreateBuilder(args);

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

// Configure SSH settings
builder.Services.Configure<SshConfiguration>(builder.Configuration.GetSection("SshConfiguration"));

// Configure monitoring options
builder.Services.Configure<RemoteMonitorOptions>(builder.Configuration.GetSection("RemoteMonitor"));

// Register application services
builder.Services.AddScoped<ISshService, SshClientService>();
builder.Services.AddScoped<IQueueService, QueueService>();
builder.Services.AddSingleton<RemotePathMonitorService>();
builder.Services.AddHostedService<RemotePathMonitorService>();
// TODO: Register concrete implementations when Infrastructure project is added
// builder.Services.AddScoped<IFileTrackingService, FileTrackingService>();

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
