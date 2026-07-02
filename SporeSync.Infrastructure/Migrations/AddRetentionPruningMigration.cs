using FluentMigrator;

namespace SporeSync.Infrastructure.Migrations;

[Migration(202607010001)]
public sealed class AddRetentionPruningMigration : Migration
{
    private const string UpPostFunctionPrefix = "SporeSync.Infrastructure.Migrations.Schema.Functions.202607010001_AddRetentionPruning.Up.Post.";
    private const string DownPreFunctionPrefix = "SporeSync.Infrastructure.Migrations.Schema.Functions.202607010001_AddRetentionPruning.Down.Pre.";

    public override void Up()
    {
        var assembly = typeof(AddRetentionPruningMigration).Assembly;
        EmbeddedSqlScripts.ExecuteSqlScripts(
            this,
            EmbeddedSqlScripts.ReadEmbeddedScriptsByPrefix(assembly, UpPostFunctionPrefix));
    }

    public override void Down()
    {
        var assembly = typeof(AddRetentionPruningMigration).Assembly;
        EmbeddedSqlScripts.ExecuteSqlScripts(
            this,
            EmbeddedSqlScripts.ReadEmbeddedScriptsByPrefix(assembly, DownPreFunctionPrefix));
    }
}
