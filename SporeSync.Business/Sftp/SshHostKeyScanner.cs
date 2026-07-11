using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Renci.SshNet;
using Renci.SshNet.Common;

namespace SporeSync.Business.Sftp;

public interface ISshHostKeyScanner
{
    /// <summary>
    /// Connects to an SSH server just far enough to observe its host key, then aborts.
    /// No credentials are sent and no authentication is attempted.
    /// </summary>
    Task<SshHostKeyScanResult> ScanAsync(string host, int port, CancellationToken cancellationToken = default);
}

public sealed record SshHostKeyScanResult(
    string HostKeyAlgorithm,
    int KeyLength,
    string FingerprintSha256);

public sealed class SshHostKeyScanner : ISshHostKeyScanner
{
    private const string ScanUsername = "host-key-scan";

    private readonly SporeSyncOptions _options;
    private readonly ILogger<SshHostKeyScanner> _logger;

    public SshHostKeyScanner(IOptions<SporeSyncOptions> options, ILogger<SshHostKeyScanner> logger)
    {
        _options = options.Value;
        _logger = logger;
    }

    public async Task<SshHostKeyScanResult> ScanAsync(
        string host,
        int port,
        CancellationToken cancellationToken = default)
    {
        var connectionInfo = new ConnectionInfo(
            host,
            port,
            ScanUsername,
            new NoneAuthenticationMethod(ScanUsername))
        {
            Timeout = TimeSpan.FromSeconds(_options.SftpConnectionTimeoutSeconds)
        };

        using var client = new SshClient(connectionInfo);

        SshHostKeyScanResult? result = null;
        client.HostKeyReceived += (_, e) =>
        {
            result = new SshHostKeyScanResult(
                e.HostKeyName,
                e.KeyLength,
                SshHostKeyFingerprint.Normalize(e.FingerPrintSHA256));

            // Abort the handshake once the key is captured; we never authenticate.
            e.CanTrust = false;
        };

        try
        {
            await Task.Run(client.Connect, cancellationToken);
        }
        catch (SshConnectionException) when (result is not null)
        {
            // Expected: cancelling trust aborts key exchange after the host key was observed.
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Host key scan failed for {Host}:{Port}", host, port);
            throw;
        }
        finally
        {
            if (client.IsConnected)
            {
                client.Disconnect();
            }
        }

        if (result is null)
        {
            throw new InvalidOperationException(
                $"The server at {host}:{port} did not present an SSH host key.");
        }

        _logger.LogInformation(
            "Scanned host key for {Host}:{Port}: {Algorithm} {KeyLength} {Fingerprint}",
            host,
            port,
            result.HostKeyAlgorithm,
            result.KeyLength,
            result.FingerprintSha256);

        return result;
    }
}
