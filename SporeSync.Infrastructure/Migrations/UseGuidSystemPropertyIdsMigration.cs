using FluentMigrator;

namespace SporeSync.Infrastructure.Migrations;

[Migration(202605280002)]
public sealed class UseGuidSystemPropertyIdsMigration : Migration
{
    private const string UpScript = "SporeSync.Infrastructure.Migrations.Schema.202605280002_UseGuidSystemPropertyIds_up.sql";
    private const string DownScript = "SporeSync.Infrastructure.Migrations.Schema.202605280002_UseGuidSystemPropertyIds_down.sql";
    private const string UpPreFunctionPrefix = "SporeSync.Infrastructure.Migrations.Functions.202605280002_UseGuidSystemPropertyIds.Up.Pre.";
    private const string UpPostFunctionPrefix = "SporeSync.Infrastructure.Migrations.Functions.202605280002_UseGuidSystemPropertyIds.Up.Post.";
    private const string DownPreFunctionPrefix = "SporeSync.Infrastructure.Migrations.Functions.202605280002_UseGuidSystemPropertyIds.Down.Pre.";
    private const string DownPostFunctionPrefix = "SporeSync.Infrastructure.Migrations.Functions.202605280002_UseGuidSystemPropertyIds.Down.Post.";

    public override void Up()
    {
        var assembly = typeof(UseGuidSystemPropertyIdsMigration).Assembly;
        EmbeddedSqlScripts.ExecuteSqlScripts(
            this,
            EmbeddedSqlScripts.ReadEmbeddedScriptsByPrefix(assembly, UpPreFunctionPrefix));
        EmbeddedSqlScripts.ExecuteSqlScript(this, EmbeddedSqlScripts.ReadEmbeddedScript(assembly, UpScript));
        EmbeddedSqlScripts.ExecuteSqlScripts(
            this,
            EmbeddedSqlScripts.ReadEmbeddedScriptsByPrefix(assembly, UpPostFunctionPrefix));
    }

    public override void Down()
    {
        var assembly = typeof(UseGuidSystemPropertyIdsMigration).Assembly;
        EmbeddedSqlScripts.ExecuteSqlScripts(
            this,
            EmbeddedSqlScripts.ReadEmbeddedScriptsByPrefix(assembly, DownPreFunctionPrefix));
        EmbeddedSqlScripts.ExecuteSqlScript(this, EmbeddedSqlScripts.ReadEmbeddedScript(assembly, DownScript));
        EmbeddedSqlScripts.ExecuteSqlScripts(
            this,
            EmbeddedSqlScripts.ReadEmbeddedScriptsByPrefix(assembly, DownPostFunctionPrefix));
    }
}
