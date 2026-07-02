using System.Reflection;
using FluentMigrator;

namespace SporeSync.Infrastructure.Migrations;

/// <summary>
/// Base class for migrations whose SQL lives in embedded resources instead of inline strings.
///
/// Scripts are resolved by convention from the migration version and class name
/// (e.g. version 202605280001 + AddRepositoryFunctionsMigration => 202605280001_AddRepositoryFunctions):
/// <list type="bullet">
/// <item>Schema scripts: Migrations/Schema/{version}_{Name}_up.sql and _down.sql (optional, must exist as a pair).</item>
/// <item>Function scripts: Migrations/Schema/Functions/{version}_{Name}/{Up|Down}/{Pre|Post}/NNN_*.sql,
/// executed in name order, Pre before the schema script and Post after it.</item>
/// </list>
/// </summary>
public abstract class EmbeddedSqlScriptMigration : Migration
{
    private const string SchemaResourceRoot = "SporeSync.Infrastructure.Migrations.Schema";
    private const string ClassNameSuffix = "Migration";

    public sealed override void Up()
    {
        foreach (var script in ResolveUpScripts())
        {
            Execute.Sql(script);
        }
    }

    public sealed override void Down()
    {
        foreach (var script in ResolveDownScripts())
        {
            Execute.Sql(script);
        }
    }

    public IReadOnlyList<string> ResolveUpScripts() => ResolveScripts("Up");

    public IReadOnlyList<string> ResolveDownScripts() => ResolveScripts("Down");

    public string ScriptBaseName
    {
        get
        {
            var type = GetType();
            var attribute = type.GetCustomAttribute<MigrationAttribute>()
                ?? throw new InvalidOperationException(
                    $"Migration '{type.Name}' is missing the [Migration] attribute required to resolve embedded SQL scripts.");

            var name = type.Name.EndsWith(ClassNameSuffix, StringComparison.Ordinal)
                ? type.Name[..^ClassNameSuffix.Length]
                : type.Name;

            return $"{attribute.Version}_{name}";
        }
    }

    private IReadOnlyList<string> ResolveScripts(string direction)
    {
        var assembly = GetType().Assembly;
        var baseName = ScriptBaseName;
        var scripts = new List<string>();

        scripts.AddRange(EmbeddedSqlScripts.ReadEmbeddedScriptsByPrefix(
            assembly,
            $"{SchemaResourceRoot}.Functions.{baseName}.{direction}.Pre."));

        var schemaScript = ResolveSchemaScript(assembly, baseName, direction);
        if (schemaScript is not null)
        {
            scripts.Add(schemaScript);
        }

        scripts.AddRange(EmbeddedSqlScripts.ReadEmbeddedScriptsByPrefix(
            assembly,
            $"{SchemaResourceRoot}.Functions.{baseName}.{direction}.Post."));

        if (scripts.Count == 0)
        {
            throw new InvalidOperationException(
                $"Migration '{GetType().Name}' resolved no embedded {direction} SQL scripts for '{baseName}'.");
        }

        return scripts;
    }

    private string? ResolveSchemaScript(Assembly assembly, string baseName, string direction)
    {
        var upResource = $"{SchemaResourceRoot}.{baseName}_up.sql";
        var downResource = $"{SchemaResourceRoot}.{baseName}_down.sql";
        var upScript = EmbeddedSqlScripts.TryReadEmbeddedScript(assembly, upResource);
        var downScript = EmbeddedSqlScripts.TryReadEmbeddedScript(assembly, downResource);

        if (upScript is null != downScript is null)
        {
            throw new InvalidOperationException(
                $"Migration '{GetType().Name}' has an unpaired schema script: both '{upResource}' and '{downResource}' must exist together.");
        }

        return direction == "Up" ? upScript : downScript;
    }
}
