using System.Diagnostics;
using System.Net.Sockets;
using Microsoft.Extensions.Logging;
using Renci.SshNet.Common;
using SporeSync.Business.Interface;
using SporeSync.Business.Sftp;
using SporeSync.Domain.Interface;
using SporeSync.Domain.Model;

namespace SporeSync.Business.Service;

public sealed class SftpConnectionTestService : ISftpConnectionTestService
{
    private readonly ISftpConnectionProfileRepository _profileRepository;
    private readonly ISftpClientFactory _clientFactory;
    private readonly ISecretProtector _secretProtector;
    private readonly ILogger<SftpConnectionTestService> _logger;

    public SftpConnectionTestService(
        ISftpConnectionProfileRepository profileRepository,
        ISftpClientFactory clientFactory,
        ISecretProtector secretProtector,
        ILogger<SftpConnectionTestService> logger)
    {
        _profileRepository = profileRepository;
        _clientFactory = clientFactory;
        _secretProtector = secretProtector;
        _logger = logger;
    }

    public async Task<SftpConnectionTestResult> TestAsync(
        SftpConnectionTestRequest request,
        CancellationToken cancellationToken = default)
    {
        var stored = request.ProfileId is Guid id
            ? await _profileRepository.GetByIdAsync(id, cancellationToken)
            : null;
        if (request.ProfileId is not null && stored is null)
        {
            return new SftpConnectionTestResult { ProfileFound = false };
        }

        var canReuseStoredCredentials = IsStoredEndpoint(request, stored);
        var (encryptedPassword, encryptedPrivateKey, encryptedPrivateKeyPassphrase) =
            ResolveAuthentication(request, stored, canReuseStoredCredentials);
        var profile = new SftpConnectionProfile
        {
            Id = stored?.Id ?? Guid.Empty,
            Name = stored?.Name ?? "Connection test",
            Host = request.Host.Trim(),
            Port = request.Port,
            Username = request.Username.Trim(),
            EncryptedPassword = encryptedPassword,
            EncryptedPrivateKey = encryptedPrivateKey,
            EncryptedPrivateKeyPassphrase = encryptedPrivateKeyPassphrase,
            TrustedHostKeyFingerprintsSha256 = ResolveFingerprints(
                request.HostKeyFingerprintSha256,
                request.TrustedHostKeyFingerprintsSha256,
                stored,
                canReuseStoredCredentials),
            IsDefault = false
        };

        var stopwatch = Stopwatch.StartNew();
        if (string.IsNullOrWhiteSpace(profile.EncryptedPassword) &&
            string.IsNullOrWhiteSpace(profile.EncryptedPrivateKey))
        {
            return Failure("authentication", "Authentication failed. Check the username and credentials.", stopwatch);
        }

        try
        {
            await using var connected = await _clientFactory.ConnectAsync(profile, cancellationToken);
            if (!string.IsNullOrWhiteSpace(request.SourcePath) &&
                !await connected.Client.ExistsAsync(request.SourcePath.Trim(), cancellationToken))
            {
                return Failure("path", "The source path does not exist or is not accessible.", stopwatch);
            }

            return new SftpConnectionTestResult
            {
                ProfileFound = true,
                Success = true,
                Message = string.IsNullOrWhiteSpace(request.SourcePath)
                    ? "Connection and authentication succeeded."
                    : "Connection, authentication, and source path check succeeded.",
                DurationMs = stopwatch.ElapsedMilliseconds
            };
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (SshHostKeyMismatchException)
        {
            return Failure("host_key", "The server host key does not match the pinned fingerprint.", stopwatch);
        }
        catch (SshAuthenticationException)
        {
            return Failure("authentication", "Authentication failed. Check the username and credentials.", stopwatch);
        }
        catch (Exception ex) when (ex is SftpPathNotFoundException or SftpPermissionDeniedException)
        {
            return Failure("path", "The source path does not exist or is not accessible.", stopwatch);
        }
        catch (Exception ex) when (ex is SocketException or SshConnectionException)
        {
            return Failure("connection", "Could not connect to the SFTP server.", stopwatch);
        }
        catch (SshOperationTimeoutException)
        {
            return Failure("connection", "The SFTP connection or operation timed out.", stopwatch);
        }
        catch (SshException)
        {
            return Failure("authentication", "Authentication failed. Check the username and credentials.", stopwatch);
        }
        catch (Exception)
        {
            return Failure("connection", "The SFTP connection test failed.", stopwatch);
        }
    }

    private SftpConnectionTestResult Failure(string type, string message, Stopwatch stopwatch)
    {
        _logger.LogWarning("SFTP connection test failed with category {FailureType}", type);
        return new SftpConnectionTestResult
        {
            ProfileFound = true,
            FailureType = type,
            Message = message,
            DurationMs = stopwatch.ElapsedMilliseconds
        };
    }

    private string? Protect(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : _secretProtector.Protect(value);

    private (string? Password, string? PrivateKey, string? Passphrase) ResolveAuthentication(
        SftpConnectionTestRequest requested,
        SftpConnectionProfile? stored,
        bool canReuseStoredCredentials)
    {
        if (requested.AuthenticationMethod == SftpAuthenticationMethod.Password)
        {
            return (
                Protect(requested.Password) ?? (canReuseStoredCredentials ? stored?.EncryptedPassword : null),
                null,
                null);
        }

        return (
            null,
            Protect(requested.PrivateKey) ?? (canReuseStoredCredentials ? stored?.EncryptedPrivateKey : null),
            requested.RemovePrivateKeyPassphrase
                ? null
                : Protect(requested.PrivateKeyPassphrase) ??
                  (canReuseStoredCredentials ? stored?.EncryptedPrivateKeyPassphrase : null));
    }

    private static bool IsStoredEndpoint(
        SftpConnectionTestRequest requested,
        SftpConnectionProfile? stored) =>
        stored is not null &&
        string.Equals(requested.Host.Trim(), stored.Host.Trim(), StringComparison.OrdinalIgnoreCase) &&
        requested.Port == stored.Port &&
        string.Equals(requested.Username.Trim(), stored.Username.Trim(), StringComparison.Ordinal);

    private static IReadOnlyList<string> ResolveFingerprints(
        string? requested,
        IReadOnlyList<string>? requestedTrusted,
        SftpConnectionProfile? stored,
        bool preserveStoredFingerprint)
    {
        if (requestedTrusted is not null)
        {
            return requestedTrusted
                .Where(fingerprint => !string.IsNullOrWhiteSpace(fingerprint))
                .Select(SshHostKeyFingerprint.Normalize)
                .ToArray();
        }

        if (string.IsNullOrWhiteSpace(requested))
        {
            return preserveStoredFingerprint
                ? stored?.TrustedHostKeyFingerprintsSha256 ?? []
                : [];
        }

        return [SshHostKeyFingerprint.Normalize(requested)];
    }
}
