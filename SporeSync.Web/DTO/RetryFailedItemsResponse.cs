namespace SporeSync.Web.DTO;

public sealed record RetryFailedItemsResponse(
    int RetriedCount,
    SporeSyncRunResponse Run);
