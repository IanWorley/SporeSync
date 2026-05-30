using FluentMigrator.Runner;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using SftpSync.Domain.Interface;
using SftpSync.Infrastructure.Logging;
using SftpSync.Infrastructure.Repository;

namespace SftpSync.Infrastructure;

public static class ServiceExtension
{
    public static IServiceCollection RegisterInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("DefaultConnection")
            ?? throw new InvalidOperationException("Connection string 'DefaultConnection' is not configured.");

        services.AddSingleton(_ => new NpgsqlDataSourceBuilder(connectionString).Build());
        services.AddSingleton<DbLoggingConfiguration>();
        services.AddSingleton<DbCallLogBuffer>();
        services.AddScoped<ISftpConnectionProfileRepository, SftpConnectionProfileRepository>();
        services.AddScoped<ISftpSyncJobRepository, SftpSyncJobRepository>();
        services.AddScoped<ISftpSyncRunRepository, SftpSyncRunRepository>();
        services.AddScoped<IDownloadQueueItemRepository, DownloadQueueItemRepository>();
        services.AddScoped<ISystemPropertyRepository, SystemPropertyRepository>();

        services
            .AddFluentMigratorCore()
            .ConfigureRunner(runner => runner
                .AddPostgres()
                .WithGlobalConnectionString(connectionString)
                .ScanIn(typeof(ServiceExtension).Assembly).For.Migrations())
            .AddLogging(logging => logging.AddFluentMigratorConsole());

        return services;
    }
}
