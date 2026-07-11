using FluentMigrator;

namespace SporeSync.Infrastructure.Migrations;

[Migration(202607110009)]
public sealed class SafeConfigurationDeletionMigration : Migration
{
    private const string UpPrefix = "SporeSync.Infrastructure.Migrations.Schema.Functions.202607110009_SafeConfigurationDeletion.Up.Post.";
    private const string DownPrefix = "SporeSync.Infrastructure.Migrations.Schema.Functions.202607110009_SafeConfigurationDeletion.Down.Pre.";

    public override void Up()
    {
        EmbeddedSqlScripts.ExecuteSqlScripts(
            this,
            EmbeddedSqlScripts.ReadEmbeddedScriptsByPrefix(typeof(SafeConfigurationDeletionMigration).Assembly, UpPrefix));
    }

    public override void Down()
    {
        EmbeddedSqlScripts.ExecuteSqlScripts(
            this,
            EmbeddedSqlScripts.ReadEmbeddedScriptsByPrefix(typeof(SafeConfigurationDeletionMigration).Assembly, DownPrefix));
    }
}
