using Microsoft.Extensions.DependencyInjection;
using SftpSync.Business.Interface;
using SftpSync.Business.Service;

namespace SftpSync.Business;

public static class ServiceExtension
{
    public static IServiceCollection RegisterBusinessLogic(this IServiceCollection services)
    {
        services.AddSingleton<ISecretProtector, SecretProtector>();
        services.AddScoped<ISftpConnectionProfileService, SftpConnectionProfileService>();
        services.AddScoped<ISftpSyncJobService, SftpSyncJobService>();
        services.AddScoped<ISystemPropertyService, SystemPropertyService>();

        return services;
    }
}
