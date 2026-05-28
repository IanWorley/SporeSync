using Microsoft.Extensions.DependencyInjection;
using SftpSync.Business.Interface;
using SftpSync.Business.Service;

namespace SftpSync.Business;

public static class ServiceExtension
{
    public static IServiceCollection RegisterBusinessLogic(this IServiceCollection services)
    {
        services.AddSingleton<IEncryptionKeyProvider, EncryptionKeyProvider>();
        services.AddSingleton<ISecretProtector, SecretProtector>();
        services.AddScoped<IEncryptionKeyInitializer, EncryptionKeyInitializer>();
        services.AddScoped<ISftpConnectionProfileService, SftpConnectionProfileService>();
        services.AddScoped<ISftpSyncJobService, SftpSyncJobService>();
        services.AddScoped<ISftpSyncRunService, SftpSyncRunService>();
        services.AddScoped<IDownloadQueueItemService, DownloadQueueItemService>();
        services.AddScoped<ISystemPropertyService, SystemPropertyService>();

        return services;
    }
}
