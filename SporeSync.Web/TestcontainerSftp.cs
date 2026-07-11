using System.Text;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;

namespace SporeSync.Web;

/// <summary>
/// Optional development-time SFTP server (atmoz/sftp) started when
/// <c>Testcontainers:Sftp:Enabled</c> is true. It is pre-seeded with sample
/// files so the full sync pipeline can be exercised end to end from the UI or
/// REST API without provisioning a real SFTP host. Used by the
/// "SporeSync.Web Agent" launch profile for browser-driven feature testing.
/// </summary>
public sealed class TestcontainerSftp : IAsyncDisposable
{
    private const string ConfigurationSection = "Testcontainers:Sftp";
    private const string RemoteRoot = "/upload";

    private static readonly (string Path, string Content)[] SeedFiles =
    [
        ($"{RemoteRoot}/welcome.txt", "Welcome to the SporeSync development SFTP server.\n"),
        ($"{RemoteRoot}/reports/2026/january.csv", "id,value\n1,100\n2,250\n"),
        ($"{RemoteRoot}/reports/2026/february.csv", "id,value\n3,75\n4,410\n"),
        ($"{RemoteRoot}/media/show-one/episode-01.mkv", "fake video payload one\n"),
        ($"{RemoteRoot}/media/show-one/episode-02.mkv", "fake video payload two\n"),
    ];

    private readonly IContainer _container;

    private TestcontainerSftp(IContainer container, string username, string password)
    {
        _container = container;
        Username = username;
        Password = password;
    }

    public string Username { get; }

    public string Password { get; }

    public string Host => _container.Hostname;

    public int Port => _container.GetMappedPublicPort(22);

    public string RemotePath => RemoteRoot;

    public static async Task<TestcontainerSftp?> StartIfEnabledAsync(
        IConfiguration configuration,
        CancellationToken cancellationToken = default)
    {
        if (!configuration.GetValue<bool>($"{ConfigurationSection}:Enabled"))
        {
            return null;
        }

        var image = configuration[$"{ConfigurationSection}:Image"] ?? "atmoz/sftp:alpine";
        var username = configuration[$"{ConfigurationSection}:Username"] ?? "demo";
        var password = configuration[$"{ConfigurationSection}:Password"] ?? "demo-password";

        var container = new ContainerBuilder(image)
            .WithCommand($"{username}:{password}:::upload")
            .WithPortBinding(22, true)
            .WithWaitStrategy(Wait.ForUnixContainer().UntilMessageIsLogged("Server listening on"))
            .Build();

        await container.StartAsync(cancellationToken);

        var sftp = new TestcontainerSftp(container, username, password);
        await sftp.SeedSampleFilesAsync(cancellationToken);
        return sftp;
    }

    public string DescribeConnection()
    {
        return $"host=localhost port={Port} username={Username} password={Password} remote path={RemotePath}";
    }

    public ValueTask DisposeAsync()
    {
        return _container.DisposeAsync();
    }

    private async Task SeedSampleFilesAsync(CancellationToken cancellationToken)
    {
        foreach (var (path, content) in SeedFiles)
        {
            // atmoz/sftp chroots users into /home/<user>; paths seen by SFTP
            // clients are relative to that chroot.
            var containerPath = $"/home/{Username}{path}";
            var directory = containerPath[..containerPath.LastIndexOf('/')];

            await ExecAsync(["mkdir", "-p", directory], cancellationToken);
            await _container.CopyAsync(Encoding.UTF8.GetBytes(content), containerPath, ct: cancellationToken);
            await ExecAsync(["chmod", "644", containerPath], cancellationToken);
        }
    }

    private async Task ExecAsync(string[] command, CancellationToken cancellationToken)
    {
        var result = await _container.ExecAsync(command, cancellationToken);
        if (result.ExitCode != 0)
        {
            throw new InvalidOperationException(
                $"SFTP container command '{string.Join(' ', command)}' failed with exit code {result.ExitCode}: {result.Stderr}");
        }
    }
}
