using Microsoft.Extensions.Options;
using SporeSync.Business;
using SporeSync.Business.Security;

namespace SporeSync.Business.Tests;

public sealed class LocalDestinationPathSandboxTests
{
    [Fact]
    public void RequireContainedPath_ReturnsCanonicalPath_WhenPathIsInsideRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), "sporesync-sandbox");
        var sandbox = CreateSandbox(root);
        var path = Path.Combine(root, "incoming", ".", "file.txt");

        var result = sandbox.RequireContainedPath(path);

        Assert.Equal(Path.Combine(root, "incoming", "file.txt"), result);
    }

    [Fact]
    public void RequireContainedPath_Throws_WhenPathEscapesRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), "sporesync-sandbox");
        var sandbox = CreateSandbox(root);
        var path = Path.Combine(root, "..", "outside.txt");

        var exception = Assert.Throws<ArgumentException>(() => sandbox.RequireContainedPath(path));

        Assert.Contains("configured destination root", exception.Message);
    }

    [Fact]
    public void RequireContainedPath_Throws_WhenPathIsRelative()
    {
        var root = Path.Combine(Path.GetTempPath(), "sporesync-sandbox");
        var sandbox = CreateSandbox(root);

        var exception = Assert.Throws<ArgumentException>(() => sandbox.RequireContainedPath("incoming/file.txt"));

        Assert.Contains("absolute", exception.Message);
    }

    private static LocalDestinationPathSandbox CreateSandbox(string root)
    {
        return new LocalDestinationPathSandbox(Options.Create(new SporeSyncOptions
        {
            DestinationRootPath = root
        }));
    }
}
