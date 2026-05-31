using FluentMigrator;

namespace SporeSync.Infrastructure.Migrations;

[Migration(202605270001)]
public sealed class AddSporeSyncCoreMigration : Migration
{
    private const string UpScript = "SporeSync.Infrastructure.Migrations.Schema.202605270001_AddSporeSyncCore_up.sql";
    private const string DownScript = "SporeSync.Infrastructure.Migrations.Schema.202605270001_AddSporeSyncCore_down.sql";

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
        var assembly = typeof(AddSporeSyncCoreMigration).Assembly;
        using var stream = assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException($"Embedded migration script '{resourceName}' was not found.");
        using var reader = new StreamReader(stream);

        return reader.ReadToEnd();
    }
}
