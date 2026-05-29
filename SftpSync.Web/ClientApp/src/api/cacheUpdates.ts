import type { QueryClient } from "@tanstack/react-query";
import type { DownloadQueueItem, PagedResponse, SftpSyncRun } from "./types";

export function upsertRunInPagedCaches(
  queryClient: QueryClient,
  run: SftpSyncRun,
) {
  queryClient.setQueriesData<PagedResponse<SftpSyncRun>>(
    { queryKey: ["runs"] },
    (page) => {
      if (!page) {
        return page;
      }

      const exists = page.items.some((item) => item.id === run.id);
      return {
        ...page,
        items: exists
          ? page.items.map((item) => (item.id === run.id ? run : item))
          : [run, ...page.items],
      };
    },
  );

  queryClient.setQueryData(["runs", run.id], run);
}

export function upsertQueueItemInPagedCaches(
  queryClient: QueryClient,
  item: DownloadQueueItem,
) {
  if (!item.syncRunId) {
    return;
  }

  queryClient.setQueriesData<PagedResponse<DownloadQueueItem>>(
    { queryKey: ["runs", item.syncRunId, "queue-items"] },
    (page) => {
      if (!page) {
        return page;
      }

      const exists = page.items.some((existing) => existing.id === item.id);
      return {
        ...page,
        items: exists
          ? page.items.map((existing) =>
              existing.id === item.id ? item : existing,
            )
          : [item, ...page.items],
      };
    },
  );
}

export function removeQueueItemFromPagedCaches(
  queryClient: QueryClient,
  runId: string,
  queueItemId: string,
) {
  queryClient.setQueriesData<PagedResponse<DownloadQueueItem>>(
    { queryKey: ["runs", runId, "queue-items"] },
    (page) => {
      if (!page) {
        return page;
      }

      return {
        ...page,
        items: page.items.filter((item) => item.id !== queueItemId),
        totalCount: Math.max(0, page.totalCount - 1),
      };
    },
  );
}
