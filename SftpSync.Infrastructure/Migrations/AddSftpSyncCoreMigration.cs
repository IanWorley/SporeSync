using FluentMigrator;

namespace SftpSync.Infrastructure.Migrations;

[Migration(202605270001)]
public sealed class AddSftpSyncCoreMigration : Migration
{
    private const string UpScript = "SftpSync.Infrastructure.Migrations.Schema.202605270001_AddSftpSyncCore_up.sql";
    private const string DownScript = "SftpSync.Infrastructure.Migrations.Schema.202605270001_AddSftpSyncCore_down.sql";

    public override void Up()
    {
        Execute.Sql(ReadEmbeddedScript(UpScript));
    }

    public override void Down()
    {
        Execute.Sql(ReadEmbeddedScript(DownScript));
    }

    private static string ReadEmbeddedScript(string resourceName)
    {
        var assembly = typeof(AddSftpSyncCoreMigration).Assembly;
        using var stream = assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException($"Embedded migration script '{resourceName}' was not found.");
        using var reader = new StreamReader(stream);

        return reader.ReadToEnd();
    }
}
