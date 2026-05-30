using FluentMigrator;

namespace SftpSync.Infrastructure.Migrations;

[Migration(202605260002)]
public sealed class AddAuditAndCoreSchemasMigration : Migration
{
    private const string UpScript = "SftpSync.Infrastructure.Migrations.Schema.202605260002_AddAuditAndCoreSchemas_up.sql";
    private const string DownScript = "SftpSync.Infrastructure.Migrations.Schema.202605260002_AddAuditAndCoreSchemas_down.sql";
    private const string UpPostFunctionPrefix = "SftpSync.Infrastructure.Migrations.Schema.Functions.202605260002_AddAuditAndCoreSchemas.Up.Post.";
    private const string DownPreFunctionPrefix = "SftpSync.Infrastructure.Migrations.Schema.Functions.202605260002_AddAuditAndCoreSchemas.Down.Pre.";

    public override void Up()
    {
        var assembly = typeof(AddAuditAndCoreSchemasMigration).Assembly;
        EmbeddedSqlScripts.ExecuteSqlScript(this, EmbeddedSqlScripts.ReadEmbeddedScript(assembly, UpScript));
        EmbeddedSqlScripts.ExecuteSqlScripts(
            this,
            EmbeddedSqlScripts.ReadEmbeddedScriptsByPrefix(assembly, UpPostFunctionPrefix));
    }

    public override void Down()
    {
        var assembly = typeof(AddAuditAndCoreSchemasMigration).Assembly;
        EmbeddedSqlScripts.ExecuteSqlScripts(
            this,
            EmbeddedSqlScripts.ReadEmbeddedScriptsByPrefix(assembly, DownPreFunctionPrefix));
        EmbeddedSqlScripts.ExecuteSqlScript(this, EmbeddedSqlScripts.ReadEmbeddedScript(assembly, DownScript));
    }
}
