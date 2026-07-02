using FluentMigrator;

namespace SporeSync.Infrastructure.Migrations;

[Migration(202605300004)]
public sealed class BackfillSystemPropertyFunctionsMigration : EmbeddedSqlScriptMigration;
