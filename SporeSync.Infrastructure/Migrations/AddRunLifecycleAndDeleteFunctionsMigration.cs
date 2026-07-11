using FluentMigrator;

namespace SporeSync.Infrastructure.Migrations;

[Migration(202607110002)]
public sealed class AddRunLifecycleAndDeleteFunctionsMigration : Migration
{
    private const string UpPostFunctionPrefix = "SporeSync.Infrastructure.Migrations.Schema.Functions.202607110002_AddRunLifecycleAndDeleteFunctions.Up.Post.";
    private const string DownPreFunctionPrefix = "SporeSync.Infrastructure.Migrations.Schema.Functions.202607110002_AddRunLifecycleAndDeleteFunctions.Down.Pre.";

    public override void Up()
    {
        var assembly = typeof(AddRunLifecycleAndDeleteFunctionsMigration).Assembly;
        EmbeddedSqlScripts.ExecuteSqlScripts(
            this,
            EmbeddedSqlScripts.ReadEmbeddedScriptsByPrefix(assembly, UpPostFunctionPrefix));
    }

    public override void Down()
    {
        var assembly = typeof(AddRunLifecycleAndDeleteFunctionsMigration).Assembly;
        EmbeddedSqlScripts.ExecuteSqlScripts(
            this,
            EmbeddedSqlScripts.ReadEmbeddedScriptsByPrefix(assembly, DownPreFunctionPrefix));
    }
}
