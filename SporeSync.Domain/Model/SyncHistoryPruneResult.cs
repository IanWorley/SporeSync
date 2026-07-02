namespace SporeSync.Domain.Model;

public sealed record SyncHistoryPruneResult(int PrunedRunCount, int PrunedQueueItemCount);
