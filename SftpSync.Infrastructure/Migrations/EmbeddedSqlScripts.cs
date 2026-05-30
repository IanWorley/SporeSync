using System.Reflection;
using FluentMigrator;

namespace SftpSync.Infrastructure.Migrations;

internal static class EmbeddedSqlScripts
{
    internal static string? ReadEmbeddedScript(Assembly assembly, string? resourceName)
    {
        if (string.IsNullOrWhiteSpace(resourceName))
        {
            return null;
        }

        using var stream = assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException($"Embedded migration script '{resourceName}' was not found.");
        using var reader = new StreamReader(stream);

        return reader.ReadToEnd();
    }

    internal static IReadOnlyList<string> ReadEmbeddedScriptsByPrefix(Assembly assembly, string? resourcePrefix)
    {
        if (string.IsNullOrWhiteSpace(resourcePrefix))
        {
            return Array.Empty<string>();
        }

        return assembly.GetManifestResourceNames()
            .Where(name => name.StartsWith(resourcePrefix, StringComparison.OrdinalIgnoreCase))
            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
            .Select(name => ReadEmbeddedScript(assembly, name))
            .Where(script => !string.IsNullOrWhiteSpace(script))
            .Select(script => script!)
            .ToArray();
    }

    internal static void ExecuteSqlScript(Migration migration, string? script)
    {
        if (string.IsNullOrWhiteSpace(script))
        {
            return;
        }

        migration.Execute.Sql(script);
    }

    internal static void ExecuteSqlScripts(Migration migration, IEnumerable<string> scripts)
    {
        foreach (var script in scripts)
        {
            ExecuteSqlScript(migration, script);
        }
    }
}
