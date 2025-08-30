using Microsoft.Extensions.DependencyInjection;

namespace SporeSync.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        // Register Application layer services
        // services.AddScoped<SyncService>();
        services.AddSingleton<ItemRegistry>();

        return services;
    }
}
