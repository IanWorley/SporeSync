using FluentMigrator;

namespace SftpSync.Infrastructure.Migrations;

[Migration(202605300002)]
public sealed class MarkRemoteDeletedQueueItemsMigration : Migration
{
    private const string UpScript = "SftpSync.Infrastructure.Migrations.Schema.202605300002_MarkRemoteDeletedQueueItems_up.sql";
    private const string DownScript = "SftpSync.Infrastructure.Migrations.Schema.202605300002_MarkRemoteDeletedQueueItems_down.sql";

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
        var assembly = typeof(MarkRemoteDeletedQueueItemsMigration).Assembly;
        using var stream = assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException($"Embedded migration script '{resourceName}' was not found.");
        using var reader = new StreamReader(stream);

        return reader.ReadToEnd();
    }
}
