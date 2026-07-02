using FluentMigrator;

namespace SporeSync.Infrastructure.Migrations;

/// <summary>
/// Reliability follow-ups:
/// 1. Crash recovery and queue-item leasing (lease columns, leased claim, release,
///    renewal, stale-item requeue, orphaned-run reaping).
/// 2. Enqueue/claim race fixes (claim gated on run status = downloading, atomic run
///    creation backed by a partial unique index on active runs).
/// </summary>
[Migration(202607110004)]
public sealed class AddQueueLeasesAndReliabilityMigration : Migration
{
    private const string UpScript = "SporeSync.Infrastructure.Migrations.Schema.202607110004_AddQueueLeasesAndReliability_up.sql";
    private const string DownScript = "SporeSync.Infrastructure.Migrations.Schema.202607110004_AddQueueLeasesAndReliability_down.sql";
    private const string UpPostFunctionPrefix = "SporeSync.Infrastructure.Migrations.Schema.Functions.202607110004_AddQueueLeasesAndReliability.Up.Post.";
    private const string DownPreFunctionPrefix = "SporeSync.Infrastructure.Migrations.Schema.Functions.202607110004_AddQueueLeasesAndReliability.Down.Pre.";

    public override void Up()
    {
        var assembly = typeof(AddQueueLeasesAndReliabilityMigration).Assembly;
        EmbeddedSqlScripts.ExecuteSqlScript(this, EmbeddedSqlScripts.ReadEmbeddedScript(assembly, UpScript));
        EmbeddedSqlScripts.ExecuteSqlScripts(
            this,
            EmbeddedSqlScripts.ReadEmbeddedScriptsByPrefix(assembly, UpPostFunctionPrefix));
    }

    public override void Down()
    {
        var assembly = typeof(AddQueueLeasesAndReliabilityMigration).Assembly;
        EmbeddedSqlScripts.ExecuteSqlScripts(
            this,
            EmbeddedSqlScripts.ReadEmbeddedScriptsByPrefix(assembly, DownPreFunctionPrefix));
        EmbeddedSqlScripts.ExecuteSqlScript(this, EmbeddedSqlScripts.ReadEmbeddedScript(assembly, DownScript));
    }
}
