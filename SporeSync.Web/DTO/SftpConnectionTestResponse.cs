namespace SporeSync.Web.DTO;

public sealed record SftpConnectionTestResponse(
    bool Success,
    string? Message,
    long DurationMs);
