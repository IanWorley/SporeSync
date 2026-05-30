using FluentMigrator;

namespace SftpSync.Infrastructure.Migrations;

[Migration(202605290002)]
public sealed class UpdateQueueItemFunctionsForGroupingMigration : Migration
{
    private const string UpScript = "";
    private const string DownScript = "";
    private const string UpPostFunctionPrefix = "SftpSync.Infrastructure.Migrations.Schema.Functions.202605290002_UpdateQueueItemFunctionsForGrouping.Up.Post.";
    private const string DownPreFunctionPrefix = "SftpSync.Infrastructure.Migrations.Schema.Functions.202605290002_UpdateQueueItemFunctionsForGrouping.Down.Pre.";

    public override void Up()
    {
        var assembly = typeof(UpdateQueueItemFunctionsForGroupingMigration).Assembly;
        EmbeddedSqlScripts.ExecuteSqlScript(this, EmbeddedSqlScripts.ReadEmbeddedScript(assembly, UpScript));
        EmbeddedSqlScripts.ExecuteSqlScripts(
            this,
            EmbeddedSqlScripts.ReadEmbeddedScriptsByPrefix(assembly, UpPostFunctionPrefix));
    }

    public override void Down()
    {
        var assembly = typeof(UpdateQueueItemFunctionsForGroupingMigration).Assembly;
        EmbeddedSqlScripts.ExecuteSqlScripts(
            this,
            EmbeddedSqlScripts.ReadEmbeddedScriptsByPrefix(assembly, DownPreFunctionPrefix));
        EmbeddedSqlScripts.ExecuteSqlScript(this, EmbeddedSqlScripts.ReadEmbeddedScript(assembly, DownScript));
    }
}
