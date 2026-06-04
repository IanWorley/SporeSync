using FluentMigrator.Runner;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using SporeSync.Infrastructure.Migrations;
using Testcontainers.PostgreSql;

namespace SporeSync.Business.Tests;

public sealed class RepositoryTestcontainerFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder("postgres:16-alpine")
        .WithDatabase("SporeSync")
        .WithUsername("sporesync")
        .WithPassword("sporesync")
        .Build();

    public NpgsqlDataSource DataSource { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        await _container.StartAsync();

        var connectionString = _container.GetConnectionString();
        DataSource = new NpgsqlDataSourceBuilder(connectionString).Build();

        using var serviceProvider = new ServiceCollection()
            .AddFluentMigratorCore()
            .ConfigureRunner(runner => runner
                .AddPostgres()
                .WithGlobalConnectionString(connectionString)
                .ScanIn(typeof(InitMigration).Assembly).For.Migrations())
            .AddLogging(logging => logging.AddFluentMigratorConsole())
            .BuildServiceProvider(false);

        serviceProvider.GetRequiredService<IMigrationRunner>().MigrateUp();
    }

    public async Task DisposeAsync()
    {
        await DataSource.DisposeAsync();
        await _container.DisposeAsync();
    }
}
