namespace SporeSync.Domain.Model;

public sealed class QueueItemQuery
{
    public IReadOnlyCollection<string> Statuses { get; init; } = Array.Empty<string>();

    public string? Search { get; init; }

    public string SortBy { get; init; } = "queuedAt";

    public string SortDirection { get; init; } = "desc";

    public int PageNumber { get; init; } = 1;

    public int PageSize { get; init; } = 50;
}
