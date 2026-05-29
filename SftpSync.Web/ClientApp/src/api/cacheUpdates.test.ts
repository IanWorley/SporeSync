import { QueryClient } from "@tanstack/react-query";
import { describe, expect, it } from "vitest";
import { upsertRunInPagedCaches } from "./cacheUpdates";
import type { PagedResponse, SftpSyncRun } from "./types";

function makeRun(id: string, status: string): SftpSyncRun {
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

describe("cache updates", () => {
  it("updates matching run pages and detail cache", () => {
    const queryClient = new QueryClient();
    const pending = makeRun("run-1", "Pending");
    const running = makeRun("run-1", "Running");
    const page: PagedResponse<SftpSyncRun> = {
      items: [pending],
      pageNumber: 1,
      pageSize: 25,
      totalCount: 1,
    };

    queryClient.setQueryData(["runs", { pageNumber: 1 }], page);
    upsertRunInPagedCaches(queryClient, running);

    expect(queryClient.getQueryData<SftpSyncRun>(["runs", "run-1"])).toEqual(
      running,
    );
    expect(
      queryClient.getQueryData<PagedResponse<SftpSyncRun>>([
        "runs",
        { pageNumber: 1 },
      ])?.items[0],
    ).toEqual(running);
  });
});
