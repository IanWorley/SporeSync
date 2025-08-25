using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using SporeSync.Application.Services;
using SporeSync.Domain.Interfaces;
using SporeSync.Domain.Models;
using SporeSync.Infrastructure.Configuration;
using SporeSync.Infrastructure.Services;

namespace SporeSync.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        // Configure SSH settings
        services.Configure<SshConfiguration>(configuration.GetSection("Settings:SshConfiguration"));

        // Configure RemotePathOptions
        services.Configure<RemotePathOptions>(configuration.GetSection("Settings:RemotePathOptions"));

        // Configure RemoteMonitorOptions
        services.Configure<RemoteMonitorOptions>(
            configuration.GetSection("Settings:RemoteMonitor"));

        // Register Queue Service
        services.AddSingleton<IQueueService, QueueItemService>();

        // Register SSH Service
        services.AddSingleton<ISshService, SshClientService>();

        // Register Remote Path Monitor Service as hosted service
        services.AddHostedService<RemotePathMonitorService>();
        services.AddSingleton<RemotePathMonitorService>();

        return services;
    }
}
