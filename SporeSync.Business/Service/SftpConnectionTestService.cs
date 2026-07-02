using System.Diagnostics;
using Microsoft.Extensions.Logging;
using SporeSync.Business.Interface;
using SporeSync.Business.Sftp;
using SporeSync.Domain.Interface;

namespace SporeSync.Business.Service;

public sealed class SftpConnectionTestService : ISftpConnectionTestService
{
    private readonly ISftpConnectionProfileRepository _profileRepository;
    private readonly ISftpClientFactory _clientFactory;
    private readonly ILogger<SftpConnectionTestService> _logger;

    public SftpConnectionTestService(
        ISftpConnectionProfileRepository profileRepository,
        ISftpClientFactory clientFactory,
        ILogger<SftpConnectionTestService> logger)
    {
        _profileRepository = profileRepository;
        _clientFactory = clientFactory;
        _logger = logger;
    }

    public async Task<SftpConnectionTestResult> TestAsync(
        Guid profileId,
        CancellationToken cancellationToken = default)
    {
        var profile = await _profileRepository.GetByIdAsync(profileId, cancellationToken);
        if (profile is null)
        {
            return new SftpConnectionTestResult { ProfileFound = false };
        }

        var stopwatch = Stopwatch.StartNew();
        try
        {
            await using var connected = await _clientFactory.ConnectAsync(profileId, cancellationToken);
            // Issue one lightweight operation to prove the session is usable beyond the handshake.
            _ = connected.Client.WorkingDirectory;
            stopwatch.Stop();

            return new SftpConnectionTestResult
            {
                ProfileFound = true,
                Success = true,
                DurationMs = stopwatch.ElapsedMilliseconds
            };
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            stopwatch.Stop();
            _logger.LogWarning(
                ex,
                "Connection test failed for profile {ProfileId} ({Host}:{Port})",
                profile.Id,
                profile.Host,
                profile.Port);

            return new SftpConnectionTestResult
            {
                ProfileFound = true,
                Success = false,
                ErrorMessage = ex.Message,
                DurationMs = stopwatch.ElapsedMilliseconds
            };
        }
    }
}
