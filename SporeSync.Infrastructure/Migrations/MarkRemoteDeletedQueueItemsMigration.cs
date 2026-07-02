using FluentMigrator;

namespace SporeSync.Infrastructure.Migrations;

[Migration(202605300002)]
public sealed class MarkRemoteDeletedQueueItemsMigration : EmbeddedSqlScriptMigration;
