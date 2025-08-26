using Microsoft.Extensions.DependencyInjection;

namespace SporeSync.Domain;

public static class DependencyInjection
{
    public static IServiceCollection AddDomain(this IServiceCollection services)
    {
        // Register Domain layer services (if any)
        // Domain layer typically contains interfaces and models, not services
        // But this provides a consistent pattern across all layers

        return services;
    }
}
