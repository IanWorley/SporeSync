using FluentMigrator;

namespace SporeSync.Infrastructure.Migrations;

[Migration(202607110006)]
public sealed class FixGroupedLeafRequeueMigration : Migration
{
    private const string UpScript = "SporeSync.Infrastructure.Migrations.Schema.202607110006_FixGroupedLeafRequeue_up.sql";
    private const string DownScript = "SporeSync.Infrastructure.Migrations.Schema.202607110006_FixGroupedLeafRequeue_down.sql";

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
        var assembly = typeof(FixGroupedLeafRequeueMigration).Assembly;
        using var stream = assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException($"Embedded migration script '{resourceName}' was not found.");
        using var reader = new StreamReader(stream);

        return reader.ReadToEnd();
    }
}
