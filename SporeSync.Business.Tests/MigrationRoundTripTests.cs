using FluentMigrator.Runner;
using Microsoft.Extensions.DependencyInjection;
using SporeSync.Infrastructure.Migrations;
using Testcontainers.PostgreSql;

namespace SporeSync.Business.Tests;

/// <summary>
/// Applies every migration up, rolls all of them back down, and applies them up again against a
/// real Postgres instance. This proves each migration's Down SQL actually reverts its Up SQL.
/// </summary>
public sealed class MigrationRoundTripTests
{
    [Fact]
    public async Task MigrateUpDownUp_RoundTripsCleanly()
    {
        await using var container = new PostgreSqlBuilder("postgres:16-alpine")
            .WithDatabase("SporeSync")
            .WithUsername("sporesync")
            .WithPassword("sporesync")
            .Build();
        await container.StartAsync();

        using var serviceProvider = new ServiceCollection()
            .AddFluentMigratorCore()
            .ConfigureRunner(runner => runner
                .AddPostgres()
                .WithGlobalConnectionString(container.GetConnectionString())
                .ScanIn(typeof(InitMigration).Assembly).For.Migrations())
            .AddLogging(logging => logging.AddFluentMigratorConsole())
            .BuildServiceProvider(false);

        var runner = serviceProvider.GetRequiredService<IMigrationRunner>();

        runner.MigrateUp();
        runner.MigrateDown(0);
        runner.MigrateUp();
    }
}
