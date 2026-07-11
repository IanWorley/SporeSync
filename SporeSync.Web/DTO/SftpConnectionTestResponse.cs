namespace SporeSync.Web.DTO;

public sealed record SftpConnectionTestResponse(
    bool Success,
    string? FailureType,
    string? Message,
    long DurationMs);
