using System.Text;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;

namespace SporeSync.Business.Tests.Sftp;

/// <summary>
/// Starts a real SFTP server (atmoz/sftp) for integration tests. Test files
/// are seeded into the chrooted upload directory through container exec, so
/// tests exercise the same SSH.NET code paths used in production.
/// </summary>
public sealed class SftpTestcontainerFixture : IAsyncLifetime
{
    public const string Username = "demo";
    public const string Password = "demo-password";
    public const string RemoteRoot = "/upload";

    private const string ChrootedHome = "/home/demo";

    private readonly IContainer _container = new ContainerBuilder("atmoz/sftp:alpine")
        .WithCommand($"{Username}:{Password}:::upload")
        .WithPortBinding(22, true)
        .WithWaitStrategy(Wait.ForUnixContainer().UntilMessageIsLogged("Server listening on"))
        .Build();

    public string Host => _container.Hostname;

    public int Port => _container.GetMappedPublicPort(22);

    public async Task InitializeAsync()
    {
        await _container.StartAsync();
    }

    public async Task DisposeAsync()
    {
        await _container.DisposeAsync();
    }

    /// <summary>
    /// Writes a file below the SFTP user's chroot. <paramref name="remotePath"/>
    /// is the path as seen by the SFTP client (for example "/upload/dir/file.txt").
    /// </summary>
    public async Task WriteFileAsync(string remotePath, string content)
    {
        var containerPath = ToContainerPath(remotePath);
        var directory = containerPath[..containerPath.LastIndexOf('/')];

        await ExecAsync("mkdir", "-p", directory);
        await _container.CopyAsync(Encoding.UTF8.GetBytes(content), containerPath);
        await ExecAsync("chmod", "644", containerPath);
    }

    public Task DeleteFileAsync(string remotePath)
    {
        return ExecAsync("rm", "-f", ToContainerPath(remotePath));
    }

    private static string ToContainerPath(string remotePath)
    {
        if (!remotePath.StartsWith('/'))
        {
            throw new ArgumentException("Remote path must be absolute.", nameof(remotePath));
        }

        return ChrootedHome + remotePath;
    }

    private async Task ExecAsync(params string[] command)
    {
        var result = await _container.ExecAsync(command);
        if (result.ExitCode != 0)
        {
            throw new InvalidOperationException(
                $"Container command '{string.Join(' ', command)}' failed with exit code {result.ExitCode}: {result.Stderr}");
        }
    }
}
