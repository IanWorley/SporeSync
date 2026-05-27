using FluentMigrator.Runner;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using SftpSync.Domain.Interface;
using SftpSync.Infrastructure.Interface;
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

        services.AddScoped(_ => new NpgsqlDataSourceBuilder(connectionString).Build());
        services.AddScoped<ISftpSyncJobRepository, SftpSyncJobRepository>();
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
