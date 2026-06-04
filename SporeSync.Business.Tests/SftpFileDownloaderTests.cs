using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SporeSync.Business;
using SporeSync.Business.Security;
using SporeSync.Business.Sftp;

namespace SporeSync.Business.Tests;

public sealed class SftpFileDownloaderTests
{
    [Fact]
    public async Task DownloadAsync_ReturnsFailureAndDoesNotConnect_WhenLocalPathEscapesRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), "sporesync-download-root");
        var factory = new ThrowingSftpClientFactory();
        var downloader = new SftpFileDownloader(
            factory,
            new LocalDestinationPathSandbox(Options.Create(new SporeSyncOptions
            {
                DestinationRootPath = root
            })),
            NullLogger<SftpFileDownloader>.Instance);

        var result = await downloader.DownloadAsync(
            Guid.NewGuid(),
            "/remote/file.txt",
            Path.Combine(root, "..", "outside.txt"));

        Assert.False(result.Success);
        Assert.Equal(0, factory.ConnectCalls);
        Assert.Contains("configured destination root", result.ErrorMessage);
    }

    private sealed class ThrowingSftpClientFactory : ISftpClientFactory
    {
        public int ConnectCalls { get; private set; }

        public Task<IConnectedSftpClient> ConnectAsync(
            Guid connectionProfileId,
            CancellationToken cancellationToken = default)
        {
            ConnectCalls++;
            throw new InvalidOperationException("SFTP connection should not be opened for unsafe paths.");
        }
    }
}
