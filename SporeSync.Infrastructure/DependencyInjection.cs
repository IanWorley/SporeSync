using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using SporeSync.Domain.Interfaces;
using SporeSync.Infrastructure.Services;
using SporeSync.Infrastructure.Configuration;

namespace SporeSync.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        // Configure RemoteMonitorOptions
        services.Configure<RemoteMonitorOptions>(
            configuration.GetSection("RemoteMonitoring"));

        // Register SSH Service
        services.AddScoped<ISshService, SshClientService>();

        // Register Remote Path Monitor Service as hosted service
        services.AddHostedService<RemotePathMonitorService>();
        services.AddSingleton<RemotePathMonitorService>();

        return services;
    }
}
