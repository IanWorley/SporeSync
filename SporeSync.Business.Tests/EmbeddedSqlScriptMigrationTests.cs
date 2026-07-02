using SporeSync.Infrastructure.Migrations;

namespace SporeSync.Business.Tests;

/// <summary>
/// Guards the convention that maps migrations to embedded SQL resources. If a script file is
/// renamed, misplaced, or not embedded, these tests fail without needing a database.
/// </summary>
public sealed class EmbeddedSqlScriptMigrationTests
{
    public static TheoryData<Type> SqlScriptMigrationTypes()
    {
        var types = typeof(InitMigration).Assembly.GetTypes()
            .Where(type => !type.IsAbstract && type.IsAssignableTo(typeof(EmbeddedSqlScriptMigration)))
            .OrderBy(type => type.Name)
            .ToArray();

        Assert.NotEmpty(types);

        var data = new TheoryData<Type>();
        foreach (var type in types)
        {
            data.Add(type);
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(SqlScriptMigrationTypes))]
    public void ResolvesNonEmptyUpAndDownScripts(Type migrationType)
    {
        var migration = (EmbeddedSqlScriptMigration)Activator.CreateInstance(migrationType)!;

        var upScripts = migration.ResolveUpScripts();
        var downScripts = migration.ResolveDownScripts();

        Assert.NotEmpty(upScripts);
        Assert.NotEmpty(downScripts);
        Assert.All(upScripts, script => Assert.False(string.IsNullOrWhiteSpace(script)));
        Assert.All(downScripts, script => Assert.False(string.IsNullOrWhiteSpace(script)));
    }

    [Fact]
    public void AllEmbeddedSqlResourcesBelongToAKnownMigration()
    {
        var assembly = typeof(InitMigration).Assembly;
        var baseNames = typeof(InitMigration).Assembly.GetTypes()
            .Where(type => !type.IsAbstract && type.IsAssignableTo(typeof(EmbeddedSqlScriptMigration)))
            .Select(type => ((EmbeddedSqlScriptMigration)Activator.CreateInstance(type)!).ScriptBaseName)
            .ToArray();

        var sqlResources = assembly.GetManifestResourceNames()
            .Where(name => name.EndsWith(".sql", StringComparison.OrdinalIgnoreCase));

        Assert.All(sqlResources, resource =>
            Assert.True(
                baseNames.Any(baseName => resource.Contains(baseName, StringComparison.OrdinalIgnoreCase)),
                $"Embedded SQL resource '{resource}' does not match any EmbeddedSqlScriptMigration."));
    }
}
