using FluentMigrator;

namespace SporeSync.Infrastructure.Migrations;

[Migration(202605260002)]
public sealed class AddAuditAndCoreSchemasMigration : EmbeddedSqlScriptMigration;
