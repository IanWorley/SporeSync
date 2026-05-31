using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Renci.SshNet;
using SporeSync.Business.Interface;
using SporeSync.Domain.Interface;

namespace SporeSync.Business.Sftp;

public sealed class SftpClientFactory : ISftpClientFactory
{
    private readonly ISftpConnectionProfileRepository _profileRepository;
    private readonly ISecretProtector _secretProtector;
    private readonly SporeSyncOptions _options;
    private readonly ILogger<SftpClientFactory> _logger;

    public SftpClientFactory(
        ISftpConnectionProfileRepository profileRepository,
        ISecretProtector secretProtector,
        IOptions<SporeSyncOptions> options,
        ILogger<SftpClientFactory> logger)
    {
        _profileRepository = profileRepository;
        _secretProtector = secretProtector;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<IConnectedSftpClient> ConnectAsync(
        Guid connectionProfileId,
        CancellationToken cancellationToken = default)
    {
        var profile = await _profileRepository.GetByIdAsync(connectionProfileId, cancellationToken)
            ?? throw new InvalidOperationException($"SFTP connection profile '{connectionProfileId}' was not found.");

        ConnectionInfo connectionInfo;
        if (!string.IsNullOrWhiteSpace(profile.EncryptedPrivateKey))
        {
            var keyText = _secretProtector.Unprotect(profile.EncryptedPrivateKey);
            using var keyStream = new MemoryStream(Encoding.UTF8.GetBytes(keyText));
            var privateKey = string.IsNullOrWhiteSpace(profile.EncryptedPrivateKeyPassphrase)
                ? new PrivateKeyFile(keyStream)
                : new PrivateKeyFile(
                    keyStream,
                    _secretProtector.Unprotect(profile.EncryptedPrivateKeyPassphrase!));

            connectionInfo = new ConnectionInfo(
                profile.Host,
                profile.Port,
                profile.Username,
                new PrivateKeyAuthenticationMethod(profile.Username, privateKey));
        }
        else if (!string.IsNullOrWhiteSpace(profile.EncryptedPassword))
        {
            var password = _secretProtector.Unprotect(profile.EncryptedPassword);
            connectionInfo = new ConnectionInfo(
                profile.Host,
                profile.Port,
                profile.Username,
                new PasswordAuthenticationMethod(profile.Username, password));
        }
        else
        {
            throw new InvalidOperationException(
                $"SFTP connection profile '{profile.Id}' has no configured credentials.");
        }

        connectionInfo.Timeout = TimeSpan.FromSeconds(_options.SftpConnectionTimeoutSeconds);

        var client = new SftpClient(connectionInfo)
        {
            OperationTimeout = TimeSpan.FromSeconds(_options.SftpOperationTimeoutSeconds)
        };

        try
        {
            await Task.Run(client.Connect, cancellationToken);
        }
        catch (Exception ex)
        {
            client.Dispose();
            _logger.LogError(ex, "Failed to connect to SFTP host {Host}:{Port}", profile.Host, profile.Port);
            throw;
        }

        return new ConnectedSftpClient(client);
    }

    private sealed class ConnectedSftpClient : IConnectedSftpClient
    {
        public ConnectedSftpClient(SftpClient client)
        {
            Client = client;
        }

        public SftpClient Client { get; }

        public ValueTask DisposeAsync()
        {
            if (Client.IsConnected)
            {
                Client.Disconnect();
            }

            Client.Dispose();
            return ValueTask.CompletedTask;
        }
    }
}
