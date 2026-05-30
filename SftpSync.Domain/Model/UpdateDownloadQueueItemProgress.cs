namespace SftpSync.Domain.Model;

public sealed class UpdateDownloadQueueItemProgress
{
    public required Guid Id { get; init; }

    public required string Status { get; init; }

    public required long BytesDownloaded { get; init; }

    public decimal? CurrentBytesPerSecond { get; init; }

    public string? ErrorMessage { get; init; }

    public string? HandledReason { get; init; }
}
