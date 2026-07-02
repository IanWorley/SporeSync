using System.Reflection;
using System.Text;

namespace SporeSync.Infrastructure.Migrations;

internal static class EmbeddedSqlScripts
{
    internal static string ReadEmbeddedScript(Assembly assembly, string resourceName)
    {
        return TryReadEmbeddedScript(assembly, resourceName)
            ?? throw new InvalidOperationException($"Embedded migration script '{resourceName}' was not found.");
    }

    internal static string? TryReadEmbeddedScript(Assembly assembly, string resourceName)
    {
        using var stream = assembly.GetManifestResourceStream(resourceName);
        if (stream is null)
        {
            return null;
        }

        using var reader = new StreamReader(stream);

        return reader.ReadToEnd();
    }

    internal static IReadOnlyList<string> ReadEmbeddedScriptsByPrefix(Assembly assembly, string resourcePrefix)
    {
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
            .ToArray();
    }

    /// <summary>
    /// MSBuild mangles resource folder segments that start with a digit by prefixing them with '_'
    /// (e.g. Functions/202605280001_Foo becomes Functions._202605280001_Foo), so match both spellings.
    /// </summary>
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
}
