using FluentMigrator;

namespace SftpSync.Infrastructure.Migrations;

[Migration(202605300004)]
public sealed class BackfillSystemPropertyFunctionsMigration : Migration
{
    private const string UpScript = "";
    private const string DownScript = "";
    private const string UpPostFunctionPrefix = "SftpSync.Infrastructure.Migrations.Schema.Functions.202605300004_BackfillSystemPropertyFunctions.Up.Post.";
    private const string DownPreFunctionPrefix = "SftpSync.Infrastructure.Migrations.Schema.Functions.202605300004_BackfillSystemPropertyFunctions.Down.Pre.";

    public override void Up()
    {
        var assembly = typeof(BackfillSystemPropertyFunctionsMigration).Assembly;
        EmbeddedSqlScripts.ExecuteSqlScript(this, EmbeddedSqlScripts.ReadEmbeddedScript(assembly, UpScript));
        EmbeddedSqlScripts.ExecuteSqlScripts(
            this,
            EmbeddedSqlScripts.ReadEmbeddedScriptsByPrefix(assembly, UpPostFunctionPrefix));
    }

    public override void Down()
    {
        var assembly = typeof(BackfillSystemPropertyFunctionsMigration).Assembly;
        EmbeddedSqlScripts.ExecuteSqlScripts(
            this,
            EmbeddedSqlScripts.ReadEmbeddedScriptsByPrefix(assembly, DownPreFunctionPrefix));
        EmbeddedSqlScripts.ExecuteSqlScript(this, EmbeddedSqlScripts.ReadEmbeddedScript(assembly, DownScript));
    }
}
