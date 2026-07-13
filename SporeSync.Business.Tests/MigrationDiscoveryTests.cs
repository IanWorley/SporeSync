using System.Reflection;
using FluentMigrator;
using SporeSync.Infrastructure.Migrations;

namespace SporeSync.Business.Tests;

public sealed class MigrationDiscoveryTests
{
    [Fact]
    public void InfrastructureMigrations_HaveUniqueVersions()
    {
        var duplicateVersions = typeof(AddTrustedHostKeysMigration).Assembly
            .GetTypes()
            .Select(type => type.GetCustomAttribute<MigrationAttribute>()?.Version)
            .Where(version => version.HasValue)
            .Select(version => version.GetValueOrDefault())
            .GroupBy(version => version)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .ToArray();

        Assert.Empty(duplicateVersions);
    }
}
