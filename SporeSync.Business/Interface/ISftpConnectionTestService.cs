namespace SporeSync.Business.Interface;

public sealed class SftpConnectionTestResult
{
    public required bool ProfileFound { get; init; }

    public bool Success { get; init; }

    public string? ErrorMessage { get; init; }

    public long DurationMs { get; init; }
}

public interface ISftpConnectionTestService
{
    Task<SftpConnectionTestResult> TestAsync(
        Guid profileId,
        CancellationToken cancellationToken = default);
}
