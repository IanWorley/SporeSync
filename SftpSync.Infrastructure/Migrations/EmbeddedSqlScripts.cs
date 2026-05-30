using System.Reflection;
using System.Text;
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

        var trimmedPrefix = resourcePrefix.Trim();
        var prefixes = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            trimmedPrefix,
            NormalizeResourcePrefix(trimmedPrefix),
        };

        return assembly.GetManifestResourceNames()
            .Where(name => prefixes.Any(prefix => name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)))
            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
            .Select(name => ReadEmbeddedScript(assembly, name))
            .Where(script => !string.IsNullOrWhiteSpace(script))
            .Select(script => script!)
            .ToArray();
    }

    private static string NormalizeResourcePrefix(string resourcePrefix)
    {
        var builder = new StringBuilder(resourcePrefix.Length + 4);

        for (var i = 0; i < resourcePrefix.Length; i++)
        {
            var character = resourcePrefix[i];
            if (character == '.'
                && i + 1 < resourcePrefix.Length
                && char.IsDigit(resourcePrefix[i + 1]))
            {
                builder.Append("._");
                continue;
            }

            builder.Append(character);
        }

        return builder.ToString();
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
