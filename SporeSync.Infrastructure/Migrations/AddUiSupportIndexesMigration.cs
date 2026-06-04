using FluentMigrator;

namespace SporeSync.Infrastructure.Migrations;

[Migration(202605270002)]
public sealed class AddUiSupportIndexesMigration : Migration
{
    private const string UpScript = "SporeSync.Infrastructure.Migrations.Schema.202605270002_AddUiSupportIndexes_up.sql";
    private const string DownScript = "SporeSync.Infrastructure.Migrations.Schema.202605270002_AddUiSupportIndexes_down.sql";

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
        var assembly = typeof(AddUiSupportIndexesMigration).Assembly;
        using var stream = assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException($"Embedded migration script '{resourceName}' was not found.");
        using var reader = new StreamReader(stream);

        return reader.ReadToEnd();
    }
}
