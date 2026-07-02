using FluentMigrator;

namespace SporeSync.Infrastructure.Migrations;

[Migration(202605290003)]
public sealed class ExtendQueueItemProjectionsForGroupingMigration : Migration
{
    private const string UpScript = "";
    private const string DownScript = "";
    private const string UpPostFunctionPrefix = "SporeSync.Infrastructure.Migrations.Functions.202605290003_ExtendQueueItemProjectionsForGrouping.Up.Post.";
    private const string DownPreFunctionPrefix = "SporeSync.Infrastructure.Migrations.Functions.202605290003_ExtendQueueItemProjectionsForGrouping.Down.Pre.";

    public override void Up()
    {
        var assembly = typeof(ExtendQueueItemProjectionsForGroupingMigration).Assembly;
        EmbeddedSqlScripts.ExecuteSqlScript(this, EmbeddedSqlScripts.ReadEmbeddedScript(assembly, UpScript));
        EmbeddedSqlScripts.ExecuteSqlScripts(
            this,
            EmbeddedSqlScripts.ReadEmbeddedScriptsByPrefix(assembly, UpPostFunctionPrefix));
    }

    public override void Down()
    {
        var assembly = typeof(ExtendQueueItemProjectionsForGroupingMigration).Assembly;
        EmbeddedSqlScripts.ExecuteSqlScripts(
            this,
            EmbeddedSqlScripts.ReadEmbeddedScriptsByPrefix(assembly, DownPreFunctionPrefix));
        EmbeddedSqlScripts.ExecuteSqlScript(this, EmbeddedSqlScripts.ReadEmbeddedScript(assembly, DownScript));
    }
}
