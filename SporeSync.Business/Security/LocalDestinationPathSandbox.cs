using Microsoft.Extensions.Options;

namespace SporeSync.Business.Security;

public sealed class LocalDestinationPathSandbox
{
    private readonly string _rootPath;
    private readonly string _rootPathWithSeparator;

    public LocalDestinationPathSandbox(IOptions<SporeSyncOptions> options)
    {
        _rootPath = Canonicalize(options.Value.DestinationRootPath, nameof(SporeSyncOptions.DestinationRootPath));
        _rootPathWithSeparator = Path.EndsInDirectorySeparator(_rootPath)
            ? _rootPath
            : _rootPath + Path.DirectorySeparatorChar;
    }

    public string RootPath => _rootPath;

    public string RequireContainedPath(string path, string parameterName = "path")
    {
        var fullPath = Canonicalize(path, parameterName);
        if (string.Equals(fullPath, _rootPath, PathComparison)
            || fullPath.StartsWith(_rootPathWithSeparator, PathComparison))
        {
            return fullPath;
        }

        throw new ArgumentException(
            $"Destination path must be inside the configured destination root '{_rootPath}'.",
            parameterName);
    }

    private static StringComparison PathComparison =>
        OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;

    private static string Canonicalize(string path, string parameterName)
    {
        var trimmed = path?.Trim();
        if (string.IsNullOrWhiteSpace(trimmed))
        {
            throw new ArgumentException("Destination path is required.", parameterName);
        }

        if (!Path.IsPathFullyQualified(trimmed))
        {
            throw new ArgumentException("Destination path must be absolute.", parameterName);
        }

        try
        {
            var fullPath = Path.GetFullPath(trimmed);
            return Path.TrimEndingDirectorySeparator(fullPath);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            throw new ArgumentException("Destination path is invalid.", parameterName, ex);
        }
    }
}
