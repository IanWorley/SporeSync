import { QueryClient } from "@tanstack/react-query";
import { describe, expect, it } from "vitest";
import {
  upsertQueueItemInPagedCaches,
  upsertRunInPagedCaches,
} from "./cacheUpdates";
import type { DownloadQueueItem, PagedResponse, SporeSyncRun } from "./types";

function makeRun(id: string, status: string): SporeSyncRun {
  return {
    id,
    jobId: "job-1",
    jobName: "Nightly Mirror",
    status,
    startedAt: "2026-05-27T12:00:00Z",
    completedAt: null,
    totalFileCount: 2,
    completedFileCount: 0,
    skippedFileCount: 0,
    failedFileCount: 0,
    totalBytes: 100,
    downloadedBytes: 0,
    currentBytesPerSecond: null,
    errorMessage: null,
  };
}

function makeQueueItem(
  id: string,
  overrides: Partial<DownloadQueueItem> = {},
): DownloadQueueItem {
  return {
    id,
    jobId: "job-1",
    syncRunId: "run-1",
    remotePath: "/incoming/file.csv",
    destinationPath: "/local/incoming/file.csv",
    fileSizeBytes: 100,
    remoteModifiedAt: null,
    status: "downloading",
    bytesDownloaded: 0,
    currentBytesPerSecond: null,
    retryCount: 0,
    handledReason: null,
    errorMessage: null,
    queuedAt: "2026-05-27T12:00:00Z",
    startedAt: null,
    completedAt: null,
    updatedAt: "2026-05-27T12:00:00Z",
    isGroup: false,
    groupRemotePath: null,
    childCount: 0,
    ...overrides,
  };
}

describe("cache updates", () => {
  it("updates matching run pages and detail cache", () => {
    const queryClient = new QueryClient();
    const pending = makeRun("run-1", "Pending");
    const running = makeRun("run-1", "Running");
    const page: PagedResponse<SporeSyncRun> = {
      items: [pending],
      pageNumber: 1,
      pageSize: 25,
      totalCount: 1,
    };

    queryClient.setQueryData(["runs", { pageNumber: 1 }], page);
    upsertRunInPagedCaches(queryClient, running);

    expect(queryClient.getQueryData<SporeSyncRun>(["runs", "run-1"])).toEqual(
      running,
    );
    expect(
      queryClient.getQueryData<PagedResponse<SporeSyncRun>>([
        "runs",
        { pageNumber: 1 },
      ])?.items[0],
    ).toEqual(running);
  });

  it("does not insert hidden group leaves into queue item pages", () => {
    const queryClient = new QueryClient();
    const group = makeQueueItem("group-1", {
      remotePath: "/incoming/group/",
      destinationPath: "/local/incoming/group",
      isGroup: true,
      childCount: 2,
    });
    const leaf = makeQueueItem("leaf-1", {
      remotePath: "/incoming/group/a.csv",
      destinationPath: "/local/incoming/group/a.csv",
      groupRemotePath: "/incoming/group/",
    });
    const page: PagedResponse<DownloadQueueItem> = {
      items: [group],
      pageNumber: 1,
      pageSize: 25,
      totalCount: 1,
    };

    queryClient.setQueryData(["runs", "run-1", "queue-items"], page);
    upsertQueueItemInPagedCaches(queryClient, leaf);

    expect(
      queryClient.getQueryData<PagedResponse<DownloadQueueItem>>([
        "runs",
        "run-1",
        "queue-items",
      ])?.items,
    ).toEqual([group]);
  });

  it("updates visible group progress in queue item pages", () => {
    const queryClient = new QueryClient();
    const group = makeQueueItem("group-1", {
      remotePath: "/incoming/group/",
      destinationPath: "/local/incoming/group",
      isGroup: true,
      childCount: 2,
    });
    const updatedGroup = makeQueueItem("group-1", {
      remotePath: "/incoming/group/",
      destinationPath: "/local/incoming/group",
      status: "downloading",
      bytesDownloaded: 60,
      isGroup: true,
      childCount: 2,
    });
    const page: PagedResponse<DownloadQueueItem> = {
      items: [group],
      pageNumber: 1,
      pageSize: 25,
      totalCount: 1,
    };

    queryClient.setQueryData(["runs", "run-1", "queue-items"], page);
    upsertQueueItemInPagedCaches(queryClient, updatedGroup);

    expect(
      queryClient.getQueryData<PagedResponse<DownloadQueueItem>>([
        "runs",
        "run-1",
        "queue-items",
      ])?.items[0],
    ).toEqual(updatedGroup);
  });
});
