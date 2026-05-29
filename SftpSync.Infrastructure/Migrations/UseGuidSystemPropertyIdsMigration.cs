using FluentMigrator;

namespace SftpSync.Infrastructure.Migrations;

[Migration(202605280002)]
public sealed class UseGuidSystemPropertyIdsMigration : Migration
{
    private const string UpScript = "SftpSync.Infrastructure.Migrations.Schema.202605280002_UseGuidSystemPropertyIds_up.sql";
    private const string DownScript = "SftpSync.Infrastructure.Migrations.Schema.202605280002_UseGuidSystemPropertyIds_down.sql";

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
        var assembly = typeof(UseGuidSystemPropertyIdsMigration).Assembly;
        using var stream = assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException($"Embedded migration script '{resourceName}' was not found.");
        using var reader = new StreamReader(stream);

        return reader.ReadToEnd();
    }
}
