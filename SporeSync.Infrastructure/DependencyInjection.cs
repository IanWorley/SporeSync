using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using SporeSync.Domain.Interfaces;
using SporeSync.Domain.Models;
using SporeSync.Infrastructure.Configuration;
using SporeSync.Infrastructure.Services;

namespace SporeSync.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<SettingsOptions>(configuration.GetSection("Settings"));

        // Register Queue Service
        // services.AddSingleton<IQueueService, QueueItemService>();

        // Register SSH Service
        services.AddSingleton<ISshService, SshClientService>();

        // Register Path Monitor Service
        services.AddHostedService<PathMonitorService>();

        return services;
    }
}
