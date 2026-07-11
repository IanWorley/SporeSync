using FluentMigrator;

namespace SporeSync.Infrastructure.Migrations;

[Migration(202607110005)]
public sealed class IncrementalGroupSyncMigration : Migration
{
    private const string UpScript = "";
    private const string DownScript = "";
    private const string UpPostFunctionPrefix = "SporeSync.Infrastructure.Migrations.Schema.Functions.202607110005_IncrementalGroupSync.Up.Post.";
    private const string DownPreFunctionPrefix = "SporeSync.Infrastructure.Migrations.Schema.Functions.202607110005_IncrementalGroupSync.Down.Pre.";

    public override void Up()
    {
        var assembly = typeof(IncrementalGroupSyncMigration).Assembly;
        EmbeddedSqlScripts.ExecuteSqlScript(this, EmbeddedSqlScripts.ReadEmbeddedScript(assembly, UpScript));
        EmbeddedSqlScripts.ExecuteSqlScripts(
            this,
            EmbeddedSqlScripts.ReadEmbeddedScriptsByPrefix(assembly, UpPostFunctionPrefix));
    }

    public override void Down()
    {
        var assembly = typeof(IncrementalGroupSyncMigration).Assembly;
        EmbeddedSqlScripts.ExecuteSqlScripts(
            this,
            EmbeddedSqlScripts.ReadEmbeddedScriptsByPrefix(assembly, DownPreFunctionPrefix));
        EmbeddedSqlScripts.ExecuteSqlScript(this, EmbeddedSqlScripts.ReadEmbeddedScript(assembly, DownScript));
    }
}
