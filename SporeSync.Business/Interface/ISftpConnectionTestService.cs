using SporeSync.Domain.Model;

namespace SporeSync.Business.Interface;

public sealed class SftpConnectionTestRequest
{
    public Guid? ProfileId { get; init; }
    public required string Host { get; init; }
    public int Port { get; init; }
    public required string Username { get; init; }
    public SftpAuthenticationMethod AuthenticationMethod { get; init; }
    public string? Password { get; init; }
    public string? PrivateKey { get; init; }
    public string? PrivateKeyPassphrase { get; init; }
    public bool RemovePrivateKeyPassphrase { get; init; }
    public string? HostKeyFingerprintSha256 { get; init; }
    public IReadOnlyList<string>? TrustedHostKeyFingerprintsSha256 { get; init; }
    public string? SourcePath { get; init; }
}

public sealed class SftpConnectionTestResult
{
    public required bool ProfileFound { get; init; }

    public bool Success { get; init; }

    public string? FailureType { get; init; }

    public string? Message { get; init; }

    public long DurationMs { get; init; }
}

public interface ISftpConnectionTestService
{
    Task<SftpConnectionTestResult> TestAsync(
        SftpConnectionTestRequest request,
        CancellationToken cancellationToken = default);
}
