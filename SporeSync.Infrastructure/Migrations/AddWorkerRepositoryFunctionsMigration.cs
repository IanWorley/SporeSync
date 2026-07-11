using FluentMigrator;

namespace SporeSync.Infrastructure.Migrations;

[Migration(202605300001)]
public sealed class AddWorkerRepositoryFunctionsMigration : Migration
{
    private const string UpScript = "";
    private const string DownScript = "";
    private const string UpPostFunctionPrefix = "SporeSync.Infrastructure.Migrations.Functions.202605300001_AddWorkerRepositoryFunctions.Up.Post.";
    private const string DownPreFunctionPrefix = "SporeSync.Infrastructure.Migrations.Functions.202605300001_AddWorkerRepositoryFunctions.Down.Pre.";

    public override void Up()
    {
        var assembly = typeof(AddWorkerRepositoryFunctionsMigration).Assembly;
        EmbeddedSqlScripts.ExecuteSqlScript(this, EmbeddedSqlScripts.ReadEmbeddedScript(assembly, UpScript));
        EmbeddedSqlScripts.ExecuteSqlScripts(
            this,
            EmbeddedSqlScripts.ReadEmbeddedScriptsByPrefix(assembly, UpPostFunctionPrefix));
    }

    public override void Down()
    {
        var assembly = typeof(AddWorkerRepositoryFunctionsMigration).Assembly;
        EmbeddedSqlScripts.ExecuteSqlScripts(
            this,
            EmbeddedSqlScripts.ReadEmbeddedScriptsByPrefix(assembly, DownPreFunctionPrefix));
        EmbeddedSqlScripts.ExecuteSqlScript(this, EmbeddedSqlScripts.ReadEmbeddedScript(assembly, DownScript));
    }
}
