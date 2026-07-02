using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SporeSync.Business;
using SporeSync.Business.Security;
using SporeSync.Business.Sftp;

namespace SporeSync.Business.Tests;

public sealed class SftpFileDownloaderTests : IDisposable
{
    private readonly string _root;

    public SftpFileDownloaderTests()
    {
        _root = Path.Combine(Path.GetTempPath(), $"sporesync-download-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    [Fact]
    public async Task DownloadAsync_ReturnsFailureAndDoesNotConnect_WhenLocalPathEscapesRoot()
    {
        var factory = new ThrowingSftpClientFactory();
        var downloader = CreateDownloader(factory);

        var result = await downloader.DownloadAsync(
            Guid.NewGuid(),
            "/remote/file.txt",
            Path.Combine(_root, "..", "outside.txt"));

        Assert.False(result.Success);
        Assert.Equal(0, factory.ConnectCalls);
        Assert.Contains("configured destination root", result.ErrorMessage);
    }

    [Fact]
    public async Task DownloadWithReader_FullDownload_WritesFileAndRemovesPart()
    {
        var content = CreateContent(2048);
        var remote = new FakeRemoteFileReader(content, ModifiedAgo(TimeSpan.FromMinutes(5)));
        var localPath = LocalPath("file.bin");
        var downloader = CreateDownloader();

        var result = await downloader.DownloadWithReaderAsync(remote, "/remote/file.bin", localPath);

        Assert.True(result.Success);
        Assert.Equal(content.Length, result.BytesDownloaded);
        Assert.Equal(content, await File.ReadAllBytesAsync(localPath));
        Assert.False(File.Exists(localPath + ".part"));
    }

    [Fact]
    public async Task DownloadWithReader_RemoteModifiedInsideStabilityWindow_IsDeferred()
    {
        var remote = new FakeRemoteFileReader(CreateContent(64), ModifiedAgo(TimeSpan.FromSeconds(2)));
        var localPath = LocalPath("unstable.bin");
        var downloader = CreateDownloader(stabilityWindowSeconds: 30);

        var result = await downloader.DownloadWithReaderAsync(remote, "/remote/unstable.bin", localPath);

        Assert.False(result.Success);
        Assert.True(result.Deferred);
        Assert.Equal(0, remote.OpenReadCalls);
        Assert.False(File.Exists(localPath));
        Assert.False(File.Exists(localPath + ".part"));
    }

    [Fact]
    public async Task DownloadWithReader_StabilityWindowDisabled_DownloadsRecentlyModifiedFile()
    {
        var content = CreateContent(64);
        var remote = new FakeRemoteFileReader(content, ModifiedAgo(TimeSpan.FromSeconds(1)));
        var localPath = LocalPath("recent.bin");
        var downloader = CreateDownloader(stabilityWindowSeconds: 0);

        var result = await downloader.DownloadWithReaderAsync(remote, "/remote/recent.bin", localPath);

        Assert.True(result.Success);
        Assert.Equal(content, await File.ReadAllBytesAsync(localPath));
    }

    [Fact]
    public async Task DownloadWithReader_MidTransferFailure_KeepsPartForResume()
    {
        var content = CreateContent(4096);
        var remote = new FakeRemoteFileReader(
            content,
            ModifiedAgo(TimeSpan.FromMinutes(5)),
            failAfterBytes: 1024);
        var localPath = LocalPath("partial.bin");
        var downloader = CreateDownloader();

        var result = await downloader.DownloadWithReaderAsync(remote, "/remote/partial.bin", localPath);

        Assert.False(result.Success);
        Assert.False(result.Deferred);
        Assert.False(File.Exists(localPath));
        Assert.True(File.Exists(localPath + ".part"));
        Assert.True(new FileInfo(localPath + ".part").Length > 0);
    }

    [Fact]
    public async Task DownloadWithReader_ExistingValidPart_ResumesFromOffset()
    {
        var content = CreateContent(4096);
        const int partLength = 1500;
        var remote = new FakeRemoteFileReader(content, ModifiedAgo(TimeSpan.FromMinutes(10)));
        var localPath = LocalPath("resume.bin");
        await File.WriteAllBytesAsync(localPath + ".part", content[..partLength]);

        var downloader = CreateDownloader();
        var result = await downloader.DownloadWithReaderAsync(remote, "/remote/resume.bin", localPath);

        Assert.True(result.Success);
        Assert.Equal(content.Length, result.BytesDownloaded);
        Assert.Equal(partLength, remote.LastReadStartOffset);
        Assert.Equal(content, await File.ReadAllBytesAsync(localPath));
        Assert.False(File.Exists(localPath + ".part"));
    }

    [Fact]
    public async Task DownloadWithReader_PartOlderThanRemoteModification_IsDiscarded()
    {
        var content = CreateContent(2048);
        var localPath = LocalPath("stale.bin");
        var partPath = localPath + ".part";
        await File.WriteAllBytesAsync(partPath, new byte[512]);
        // The remote file changed after the part was written: the part is not a valid prefix.
        File.SetLastWriteTimeUtc(partPath, DateTime.UtcNow.AddHours(-2));
        var remote = new FakeRemoteFileReader(content, ModifiedAgo(TimeSpan.FromMinutes(30)));

        var downloader = CreateDownloader();
        var result = await downloader.DownloadWithReaderAsync(remote, "/remote/stale.bin", localPath);

        Assert.True(result.Success);
        Assert.Equal(0, remote.LastReadStartOffset);
        Assert.Equal(content, await File.ReadAllBytesAsync(localPath));
    }

    [Fact]
    public async Task DownloadWithReader_PartLargerThanRemote_IsDiscarded()
    {
        var content = CreateContent(1024);
        var localPath = LocalPath("oversized.bin");
        await File.WriteAllBytesAsync(localPath + ".part", new byte[4096]);
        var remote = new FakeRemoteFileReader(content, ModifiedAgo(TimeSpan.FromMinutes(30)));

        var downloader = CreateDownloader();
        var result = await downloader.DownloadWithReaderAsync(remote, "/remote/oversized.bin", localPath);

        Assert.True(result.Success);
        Assert.Equal(0, remote.LastReadStartOffset);
        Assert.Equal(content, await File.ReadAllBytesAsync(localPath));
    }

    [Fact]
    public async Task DownloadWithReader_SizeMismatch_FailsAndKeepsShortPartForResume()
    {
        var content = CreateContent(1000);
        // The server reports more bytes than the stream actually delivers (truncated transfer).
        var remote = new FakeRemoteFileReader(
            content,
            ModifiedAgo(TimeSpan.FromMinutes(5)),
            reportedLength: 2000);
        var localPath = LocalPath("mismatch.bin");

        var downloader = CreateDownloader();
        var result = await downloader.DownloadWithReaderAsync(remote, "/remote/mismatch.bin", localPath);

        Assert.False(result.Success);
        Assert.Contains("Size verification failed", result.ErrorMessage);
        Assert.False(File.Exists(localPath));
        Assert.True(File.Exists(localPath + ".part"));
        Assert.Equal(content.Length, new FileInfo(localPath + ".part").Length);
    }

    [Fact]
    public async Task DownloadWithReader_PartFromFailedAttempt_IsDiscardedWhenRemoteChangesAfterAttempt()
    {
        var localPath = LocalPath("changed-after-failure.bin");
        var downloader = CreateDownloader();

        // Attempt 1: remote v1 fails mid-transfer, leaving a .part stamped with v1's mtime.
        var v1Content = CreateContent(4096);
        var v1Remote = new FakeRemoteFileReader(
            v1Content,
            ModifiedAgo(TimeSpan.FromMinutes(10)),
            failAfterBytes: 1024);
        var firstResult = await downloader.DownloadWithReaderAsync(v1Remote, "/remote/changed.bin", localPath);
        Assert.False(firstResult.Success);
        Assert.True(File.Exists(localPath + ".part"));

        // The remote file is then replaced with new content (newer mtime, larger size, so the
        // part would still look like a plausible prefix by size alone).
        var v2Content = CreateContent(8192);
        var v2Remote = new FakeRemoteFileReader(v2Content, ModifiedAgo(TimeSpan.FromMinutes(1)));

        var secondResult = await downloader.DownloadWithReaderAsync(v2Remote, "/remote/changed.bin", localPath);

        Assert.True(secondResult.Success);
        // The stale part must not be resumed: the whole v2 file is downloaded from offset 0.
        Assert.Equal(0, v2Remote.LastReadStartOffset);
        Assert.Equal(v2Content, await File.ReadAllBytesAsync(localPath));
    }

    [Fact]
    public async Task DownloadWithReader_PartFromFailedAttempt_ResumesWhenRemoteUnchanged()
    {
        var localPath = LocalPath("unchanged-after-failure.bin");
        var downloader = CreateDownloader();
        var content = CreateContent(4096);
        var modifiedAt = ModifiedAgo(TimeSpan.FromMinutes(10));

        // Attempt 1 fails mid-transfer; the kept part is stamped with the remote's mtime, so an
        // unchanged remote (same mtime) must still be resumable on the next attempt.
        var failingRemote = new FakeRemoteFileReader(content, modifiedAt, failAfterBytes: 1024);
        var firstResult = await downloader.DownloadWithReaderAsync(failingRemote, "/remote/unchanged.bin", localPath);
        Assert.False(firstResult.Success);
        var partLength = new FileInfo(localPath + ".part").Length;
        Assert.True(partLength > 0);

        var healthyRemote = new FakeRemoteFileReader(content, modifiedAt);
        var secondResult = await downloader.DownloadWithReaderAsync(healthyRemote, "/remote/unchanged.bin", localPath);

        Assert.True(secondResult.Success);
        Assert.Equal(partLength, healthyRemote.LastReadStartOffset);
        Assert.Equal(content, await File.ReadAllBytesAsync(localPath));
    }

    [Fact]
    public async Task DownloadWithReader_ZeroByteRemoteFile_Succeeds()
    {
        var remote = new FakeRemoteFileReader(Array.Empty<byte>(), ModifiedAgo(TimeSpan.FromMinutes(5)));
        var localPath = LocalPath("empty.bin");

        var downloader = CreateDownloader();
        var result = await downloader.DownloadWithReaderAsync(remote, "/remote/empty.bin", localPath);

        Assert.True(result.Success);
        Assert.Equal(0, result.BytesDownloaded);
        Assert.True(File.Exists(localPath));
        Assert.Equal(0, new FileInfo(localPath).Length);
    }

    [Fact]
    public async Task DownloadWithReader_CompletePartWithMatchingSize_CompletesWithoutReDownloading()
    {
        var content = CreateContent(2048);
        var localPath = LocalPath("complete-part.bin");
        await File.WriteAllBytesAsync(localPath + ".part", content);
        var remote = new FakeRemoteFileReader(content, ModifiedAgo(TimeSpan.FromMinutes(30)));

        var downloader = CreateDownloader();
        var result = await downloader.DownloadWithReaderAsync(remote, "/remote/complete-part.bin", localPath);

        Assert.True(result.Success);
        Assert.Equal(0, remote.OpenReadCalls);
        Assert.Equal(content, await File.ReadAllBytesAsync(localPath));
        Assert.False(File.Exists(localPath + ".part"));
    }

    private SftpFileDownloader CreateDownloader(
        ISftpClientFactory? factory = null,
        int stabilityWindowSeconds = 15)
    {
        var options = Options.Create(new SporeSyncOptions
        {
            DestinationRootPath = _root,
            RemoteFileStabilityWindowSeconds = stabilityWindowSeconds
        });

        return new SftpFileDownloader(
            factory ?? new ThrowingSftpClientFactory(),
            new LocalDestinationPathSandbox(options),
            options,
            NullLogger<SftpFileDownloader>.Instance);
    }

    private string LocalPath(string fileName) => Path.Combine(_root, fileName);

    private static byte[] CreateContent(int length)
    {
        var content = new byte[length];
        new Random(42).NextBytes(content);
        return content;
    }

    private static DateTimeOffset ModifiedAgo(TimeSpan age) => DateTimeOffset.UtcNow - age;

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

    private sealed class FakeRemoteFileReader : ISftpRemoteFileReader
    {
        private readonly byte[] _content;
        private readonly DateTimeOffset? _modifiedAt;
        private readonly long? _reportedLength;
        private readonly int? _failAfterBytes;

        public FakeRemoteFileReader(
            byte[] content,
            DateTimeOffset? modifiedAt,
            long? reportedLength = null,
            int? failAfterBytes = null)
        {
            _content = content;
            _modifiedAt = modifiedAt;
            _reportedLength = reportedLength;
            _failAfterBytes = failAfterBytes;
        }

        public int OpenReadCalls { get; private set; }

        public long LastReadStartOffset { get; private set; }

        public SftpRemoteFileInfo GetFileInfo(string remotePath) =>
            new(_reportedLength ?? _content.Length, _modifiedAt);

        public Stream OpenRead(string remotePath)
        {
            OpenReadCalls++;
            return new TrackingStream(this, _content, _failAfterBytes);
        }

        private sealed class TrackingStream : MemoryStream
        {
            private readonly FakeRemoteFileReader _owner;
            private readonly int? _failAfterBytes;
            private long _bytesRead;
            private bool _startRecorded;

            public TrackingStream(FakeRemoteFileReader owner, byte[] content, int? failAfterBytes)
                : base(content, writable: false)
            {
                _owner = owner;
                _failAfterBytes = failAfterBytes;
            }

            public override int Read(byte[] buffer, int offset, int count)
            {
                RecordStart();
                ThrowIfLimitReached();
                var read = base.Read(buffer, offset, LimitCount(count));
                _bytesRead += read;
                return read;
            }

            public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
            {
                RecordStart();
                ThrowIfLimitReached();
                var limited = _failAfterBytes is { } limit
                    ? buffer[..Math.Min(buffer.Length, (int)Math.Max(1, limit - _bytesRead))]
                    : buffer;
                var read = base.Read(limited.Span);
                _bytesRead += read;
                return ValueTask.FromResult(read);
            }

            private void RecordStart()
            {
                if (!_startRecorded)
                {
                    _owner.LastReadStartOffset = Position;
                    _startRecorded = true;
                }
            }

            private void ThrowIfLimitReached()
            {
                if (_failAfterBytes is { } limit && _bytesRead >= limit)
                {
                    throw new IOException("Simulated connection loss mid-transfer.");
                }
            }

            private int LimitCount(int count)
            {
                if (_failAfterBytes is { } limit)
                {
                    return Math.Min(count, (int)Math.Max(1, limit - _bytesRead));
                }

                return count;
            }
        }
    }
}
