using FluentMigrator;

namespace SftpSync.Infrastructure.Migrations;

[Migration(202605260001)]
public sealed class InitMigration : Migration
{
    public override void Up()
    {
        Create.Table("sftp_sync_jobs")
            .WithColumn("id").AsGuid().PrimaryKey()
            .WithColumn("name").AsString(200).NotNullable()
            .WithColumn("source_path").AsString(1000).NotNullable()
            .WithColumn("destination_path").AsString(1000).NotNullable()
            .WithColumn("is_enabled").AsBoolean().NotNullable().WithDefaultValue(false)
            .WithColumn("created_at_utc").AsDateTimeOffset().NotNullable().WithDefault(SystemMethods.CurrentUTCDateTime);
    }

    public override void Down()
    {
        Delete.Table("sftp_sync_jobs");
    }
}
