using Renci.SshNet;

namespace SporeSync.Business.Sftp;

public interface ISftpClientFactory
{
    Task<IConnectedSftpClient> ConnectAsync(
        Guid connectionProfileId,
        CancellationToken cancellationToken = default);
}

public interface IConnectedSftpClient : IAsyncDisposable
{
    SftpClient Client { get; }
}
