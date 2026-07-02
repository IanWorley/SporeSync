using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using SporeSync.Business.Interface;
using SporeSync.Business.Observability;
using SporeSync.Business.Security;
using SporeSync.Business.Service;
using SporeSync.Business.Sftp;
using SporeSync.Business.Worker;

namespace SporeSync.Business;

public static class ServiceExtension
{
    public static IServiceCollection RegisterBusinessLogic(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddOptions<SporeSyncOptions>()
            .Bind(configuration.GetSection(SporeSyncOptions.SectionName))
            .ValidateDataAnnotations()
            .Validate(
                options => Path.IsPathFullyQualified(options.DestinationRootPath),
                $"{SporeSyncOptions.SectionName}:{nameof(SporeSyncOptions.DestinationRootPath)} must be an absolute path.")
            .ValidateOnStart();
        services.AddSingleton<SporeSyncMetrics>();
        services.AddSingleton<IEncryptionKeyProvider, EncryptionKeyProvider>();
        services.AddSingleton<ISecretProtector, SecretProtector>();
        services.AddScoped<IEncryptionKeyInitializer, EncryptionKeyInitializer>();
        services.AddScoped<ISftpConnectionProfileService, SftpConnectionProfileService>();
        services.AddScoped<ISporeSyncJobService, SporeSyncJobService>();
        services.AddScoped<ISporeSyncRunService, SporeSyncRunService>();
        services.AddScoped<IDownloadQueueItemService, DownloadQueueItemService>();
        services.AddScoped<IDownloadQueueItemFileDeleteService, DownloadQueueItemFileDeleteService>();
        services.AddScoped<ISystemPropertyService, SystemPropertyService>();
        services.AddSingleton<LocalDestinationPathSandbox>();
        services.AddScoped<ISftpClientFactory, SftpClientFactory>();
        services.AddScoped<RealSftpDirectoryScanner>();
        services.AddScoped<ISftpFileDownloader, SftpFileDownloader>();
        services.AddSingleton<DownloadRetryPolicy>();
        services.AddScoped<IChangeDetector, ChangeDetector>();
        services.AddScoped<ISyncRunOrchestrator, SyncRunOrchestrator>();
        services.AddScoped<ISyncJobRunService, SyncJobRunService>();
        services.AddHostedService<JobSchedulerHostedService>();
        services.AddHostedService<DownloadWorkerHostedService>();
        services.AddHostedService<RetentionPruningHostedService>();

        return services;
    }
}
