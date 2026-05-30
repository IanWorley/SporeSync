using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using SftpSync.Business.Interface;
using SftpSync.Business.Service;
using SftpSync.Business.Sftp;
using SftpSync.Business.Worker;

namespace SftpSync.Business;

public static class ServiceExtension
{
    public static IServiceCollection RegisterBusinessLogic(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.Configure<SftpSyncOptions>(configuration.GetSection(SftpSyncOptions.SectionName));
        services.AddSingleton<IEncryptionKeyProvider, EncryptionKeyProvider>();
        services.AddSingleton<ISecretProtector, SecretProtector>();
        services.AddScoped<IEncryptionKeyInitializer, EncryptionKeyInitializer>();
        services.AddScoped<ISftpConnectionProfileService, SftpConnectionProfileService>();
        services.AddScoped<ISftpSyncJobService, SftpSyncJobService>();
        services.AddScoped<ISftpSyncRunService, SftpSyncRunService>();
        services.AddScoped<IDownloadQueueItemService, DownloadQueueItemService>();
        services.AddScoped<IDownloadQueueItemFileDeleteService, DownloadQueueItemFileDeleteService>();
        services.AddScoped<ISystemPropertyService, SystemPropertyService>();
        services.AddScoped<ISftpClientFactory, SftpClientFactory>();
        services.AddScoped<RealSftpDirectoryScanner>();
        services.AddScoped<SftpFileDownloader>();
        services.AddScoped<IChangeDetector, ChangeDetector>();
        services.AddScoped<ISyncRunOrchestrator, SyncRunOrchestrator>();
        services.AddScoped<ISyncJobRunService, SyncJobRunService>();
        services.AddHostedService<JobSchedulerHostedService>();
        services.AddHostedService<DownloadWorkerHostedService>();

        return services;
    }
}
