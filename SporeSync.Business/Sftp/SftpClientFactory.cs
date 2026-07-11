using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Renci.SshNet;
using SporeSync.Business.Interface;
using SporeSync.Domain.Interface;
using SporeSync.Domain.Model;

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

        var pinnedFingerprint = profile.HostKeyFingerprintSha256;
        string? presentedFingerprint = null;
        string? mismatchFingerprint = null;

        client.HostKeyReceived += (_, e) =>
        {
            presentedFingerprint = SshHostKeyFingerprint.Normalize(e.FingerPrintSHA256);

            if (string.IsNullOrWhiteSpace(pinnedFingerprint))
            {
                // Trust-on-first-use: accept and pin after the connection succeeds.
                e.CanTrust = true;
                return;
            }

            if (SshHostKeyFingerprint.Matches(pinnedFingerprint, presentedFingerprint))
            {
                e.CanTrust = true;
                return;
            }

            mismatchFingerprint = presentedFingerprint;
            e.CanTrust = false;
        };

        try
        {
            await Task.Run(client.Connect, cancellationToken);
        }
        catch (Exception ex)
        {
            client.Dispose();

            if (mismatchFingerprint is not null)
            {
                var mismatch = new SshHostKeyMismatchException(
                    profile.Host,
                    profile.Port,
                    pinnedFingerprint!,
                    mismatchFingerprint);
                _logger.LogError(
                    mismatch,
                    "Rejected SFTP host {Host}:{Port}: host key fingerprint {ActualFingerprint} does not match pinned fingerprint {ExpectedFingerprint}",
                    profile.Host,
                    profile.Port,
                    mismatchFingerprint,
                    pinnedFingerprint);
                throw mismatch;
            }

            _logger.LogError(ex, "Failed to connect to SFTP host {Host}:{Port}", profile.Host, profile.Port);
            throw;
        }

        if (string.IsNullOrWhiteSpace(pinnedFingerprint) && presentedFingerprint is not null)
        {
            await PinHostKeyOnFirstUseAsync(profile, presentedFingerprint, cancellationToken);
        }

        return new ConnectedSftpClient(client);
    }

    private async Task PinHostKeyOnFirstUseAsync(
        SftpConnectionProfile profile,
        string fingerprint,
        CancellationToken cancellationToken)
    {
        try
        {
            var pinned = await _profileRepository.TryPinHostKeyFingerprintAsync(
                profile.Id,
                fingerprint,
                cancellationToken);

            if (pinned)
            {
                _logger.LogWarning(
                    "Pinned SSH host key fingerprint {Fingerprint} for SFTP host {Host}:{Port} on first use (profile '{ProfileName}'). Future connections will be rejected if the host key changes.",
                    fingerprint,
                    profile.Host,
                    profile.Port,
                    profile.Name);
            }
            else
            {
                _logger.LogInformation(
                    "Skipped first-use host key pin for SFTP profile '{ProfileName}' because the profile was already pinned or no longer exists.",
                    profile.Name);
            }
        }
        catch (Exception ex)
        {
            // The connection itself succeeded; failing to persist the pin should not fail the sync run.
            _logger.LogError(
                ex,
                "Failed to persist first-use host key fingerprint {Fingerprint} for SFTP profile '{ProfileName}'",
                fingerprint,
                profile.Name);
        }
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
