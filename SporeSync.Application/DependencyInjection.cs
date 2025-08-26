using Microsoft.Extensions.DependencyInjection;
using SporeSync.Application.Services;

namespace SporeSync.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        // Register Application layer services
        services.AddScoped<SyncService>();

        return services;
    }
}
