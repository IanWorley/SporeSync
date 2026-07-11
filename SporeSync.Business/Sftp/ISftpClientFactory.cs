using Renci.SshNet;
using SporeSync.Domain.Model;

namespace SporeSync.Business.Sftp;

public interface ISftpClientFactory
{
    Task<IConnectedSftpClient> ConnectAsync(
        Guid connectionProfileId,
        CancellationToken cancellationToken = default);

    Task<IConnectedSftpClient> ConnectAsync(
        SftpConnectionProfile profile,
        CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("Connecting a transient SFTP profile is not supported by this factory.");
}

public interface IConnectedSftpClient : IAsyncDisposable
{
    SftpClient Client { get; }

    bool IsConnected { get; }
}
