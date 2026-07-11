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

        var profile = new SftpConnectionProfile
        {
            Id = stored?.Id ?? Guid.Empty,
            Name = stored?.Name ?? "Connection test",
            Host = request.Host.Trim(),
            Port = request.Port,
            Username = request.Username.Trim(),
            EncryptedPassword = Protect(request.Password) ?? stored?.EncryptedPassword,
            EncryptedPrivateKey = Protect(request.PrivateKey) ?? stored?.EncryptedPrivateKey,
            EncryptedPrivateKeyPassphrase = Protect(request.PrivateKeyPassphrase)
                ?? stored?.EncryptedPrivateKeyPassphrase,
            HostKeyFingerprintSha256 = ResolveFingerprint(request.HostKeyFingerprintSha256, stored),
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

    private static string? ResolveFingerprint(string? requested, SftpConnectionProfile? stored)
    {
        if (requested is null)
        {
            return stored?.HostKeyFingerprintSha256;
        }

        return string.IsNullOrWhiteSpace(requested)
            ? null
            : SshHostKeyFingerprint.Normalize(requested);
    }
}
