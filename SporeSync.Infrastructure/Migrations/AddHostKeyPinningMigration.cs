using FluentMigrator;

namespace SporeSync.Infrastructure.Migrations;

[Migration(202607010001)]
public sealed class AddHostKeyPinningMigration : Migration
{
    private const string UpScript = "SporeSync.Infrastructure.Migrations.Schema.202607010001_AddHostKeyPinning_up.sql";
    private const string DownScript = "SporeSync.Infrastructure.Migrations.Schema.202607010001_AddHostKeyPinning_down.sql";
    private const string UpPostFunctionPrefix = "SporeSync.Infrastructure.Migrations.Schema.Functions.202607010001_AddHostKeyPinning.Up.Post.";
    private const string DownPreFunctionPrefix = "SporeSync.Infrastructure.Migrations.Schema.Functions.202607010001_AddHostKeyPinning.Down.Pre.";

    public override void Up()
    {
        var assembly = typeof(AddHostKeyPinningMigration).Assembly;
        EmbeddedSqlScripts.ExecuteSqlScript(this, EmbeddedSqlScripts.ReadEmbeddedScript(assembly, UpScript));
        EmbeddedSqlScripts.ExecuteSqlScripts(
            this,
            EmbeddedSqlScripts.ReadEmbeddedScriptsByPrefix(assembly, UpPostFunctionPrefix));
    }

    public override void Down()
    {
        var assembly = typeof(AddHostKeyPinningMigration).Assembly;
        EmbeddedSqlScripts.ExecuteSqlScripts(
            this,
            EmbeddedSqlScripts.ReadEmbeddedScriptsByPrefix(assembly, DownPreFunctionPrefix));
        EmbeddedSqlScripts.ExecuteSqlScript(this, EmbeddedSqlScripts.ReadEmbeddedScript(assembly, DownScript));
    }
}
