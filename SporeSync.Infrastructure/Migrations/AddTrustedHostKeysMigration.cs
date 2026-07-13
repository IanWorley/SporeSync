using FluentMigrator;

namespace SporeSync.Infrastructure.Migrations;

[Migration(202607110010)]
public sealed class AddTrustedHostKeysMigration : Migration
{
    public override void Up()
    {
        Execute.Sql("""
            CREATE TABLE core.sftp_connection_profile_trusted_host_keys (
                profile_id uuid NOT NULL REFERENCES core.sftp_connection_profiles(id) ON DELETE CASCADE,
                fingerprint_sha256 varchar(100) NOT NULL,
                PRIMARY KEY (profile_id, fingerprint_sha256)
            );
            INSERT INTO core.sftp_connection_profile_trusted_host_keys (profile_id, fingerprint_sha256)
            SELECT id, host_key_fingerprint_sha256
            FROM core.sftp_connection_profiles
            WHERE host_key_fingerprint_sha256 IS NOT NULL;
            """);
    }

    public override void Down()
    {
        Execute.Sql("""
            UPDATE core.sftp_connection_profiles profiles
            SET host_key_fingerprint_sha256 = keys.fingerprint_sha256
            FROM (
                SELECT DISTINCT ON (profile_id) profile_id, fingerprint_sha256
                FROM core.sftp_connection_profile_trusted_host_keys
                ORDER BY profile_id, fingerprint_sha256
            ) keys
            WHERE profiles.id = keys.profile_id;
            DROP TABLE core.sftp_connection_profile_trusted_host_keys;
            """);
    }
}
