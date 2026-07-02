using FluentMigrator;

namespace SporeSync.Infrastructure.Migrations;

[Migration(202607110003)]
public sealed class AddRetryBackoffAndResumeMigration : Migration
{
    private const string UpScript = "SporeSync.Infrastructure.Migrations.Schema.202607110003_AddRetryBackoffAndResume_up.sql";
    private const string DownScript = "SporeSync.Infrastructure.Migrations.Schema.202607110003_AddRetryBackoffAndResume_down.sql";
    private const string UpPostFunctionPrefix = "SporeSync.Infrastructure.Migrations.Schema.Functions.202607110003_AddRetryBackoffAndResume.Up.Post.";
    private const string DownPreFunctionPrefix = "SporeSync.Infrastructure.Migrations.Schema.Functions.202607110003_AddRetryBackoffAndResume.Down.Pre.";

    public override void Up()
    {
        var assembly = typeof(AddRetryBackoffAndResumeMigration).Assembly;
        EmbeddedSqlScripts.ExecuteSqlScript(this, EmbeddedSqlScripts.ReadEmbeddedScript(assembly, UpScript));
        EmbeddedSqlScripts.ExecuteSqlScripts(
            this,
            EmbeddedSqlScripts.ReadEmbeddedScriptsByPrefix(assembly, UpPostFunctionPrefix));
    }

    public override void Down()
    {
        var assembly = typeof(AddRetryBackoffAndResumeMigration).Assembly;
        EmbeddedSqlScripts.ExecuteSqlScripts(
            this,
            EmbeddedSqlScripts.ReadEmbeddedScriptsByPrefix(assembly, DownPreFunctionPrefix));
        EmbeddedSqlScripts.ExecuteSqlScript(this, EmbeddedSqlScripts.ReadEmbeddedScript(assembly, DownScript));
    }
}
