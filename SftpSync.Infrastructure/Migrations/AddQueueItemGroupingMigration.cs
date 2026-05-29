using FluentMigrator;

namespace SftpSync.Infrastructure.Migrations;

[Migration(202605290001)]
public sealed class AddQueueItemGroupingMigration : Migration
{
    private const string UpScript = "SftpSync.Infrastructure.Migrations.Schema.202605290001_AddQueueItemGrouping_up.sql";
    private const string DownScript = "SftpSync.Infrastructure.Migrations.Schema.202605290001_AddQueueItemGrouping_down.sql";

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
        var assembly = typeof(AddQueueItemGroupingMigration).Assembly;
        using var stream = assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException($"Embedded migration script '{resourceName}' was not found.");
        using var reader = new StreamReader(stream);

        return reader.ReadToEnd();
    }
}
