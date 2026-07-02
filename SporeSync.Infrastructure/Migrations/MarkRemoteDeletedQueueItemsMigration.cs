using FluentMigrator;

namespace SporeSync.Infrastructure.Migrations;

[Migration(202605300002)]
public sealed class MarkRemoteDeletedQueueItemsMigration : Migration
{
    private const string UpScript = "";
    private const string DownScript = "";
    private const string UpPostFunctionPrefix = "SporeSync.Infrastructure.Migrations.Schema.Functions.202605300002_MarkRemoteDeletedQueueItems.Up.Post.";
    private const string DownPreFunctionPrefix = "SporeSync.Infrastructure.Migrations.Schema.Functions.202605300002_MarkRemoteDeletedQueueItems.Down.Pre.";

    public override void Up()
    {
        var assembly = typeof(MarkRemoteDeletedQueueItemsMigration).Assembly;
        EmbeddedSqlScripts.ExecuteSqlScript(this, EmbeddedSqlScripts.ReadEmbeddedScript(assembly, UpScript));
        EmbeddedSqlScripts.ExecuteSqlScripts(
            this,
            EmbeddedSqlScripts.ReadEmbeddedScriptsByPrefix(assembly, UpPostFunctionPrefix));
    }

    public override void Down()
    {
        var assembly = typeof(MarkRemoteDeletedQueueItemsMigration).Assembly;
        EmbeddedSqlScripts.ExecuteSqlScripts(
            this,
            EmbeddedSqlScripts.ReadEmbeddedScriptsByPrefix(assembly, DownPreFunctionPrefix));
        EmbeddedSqlScripts.ExecuteSqlScript(this, EmbeddedSqlScripts.ReadEmbeddedScript(assembly, DownScript));
    }
}
