import type {
  DownloadQueueItem,
  PagedResponse,
  SftpConnectionProfile,
  SftpSyncJob,
  SftpSyncRun,
  StatusResponse,
  UpsertSftpConnectionProfile,
  UpsertSftpSyncJob,
} from "./types";

export interface PageQuery {
  status?: string[];
  search?: string;
  sortBy?: string;
  sortDirection?: "asc" | "desc";
  pageNumber?: number;
  pageSize?: number;
}

export async function fetchJson<T>(
  path: string,
  init?: RequestInit,
): Promise<T> {
  const response = await fetch(path, {
    ...init,
    headers: {
      Accept: "application/json",
      "Content-Type": "application/json",
      ...init?.headers,
    },
  });

  if (!response.ok) {
    const message = await response.text();
    throw new Error(message || `Request failed with ${response.status}`);
  }

  return response.json() as Promise<T>;
}

export function toQueryString(query: PageQuery = {}) {
  const params = new URLSearchParams();

  for (const status of query.status ?? []) {
    params.append("status", status);
  }

  if (query.search) params.set("search", query.search);
  if (query.sortBy) params.set("sortBy", query.sortBy);
  if (query.sortDirection) params.set("sortDirection", query.sortDirection);
  if (query.pageNumber) params.set("pageNumber", String(query.pageNumber));
  if (query.pageSize) params.set("pageSize", String(query.pageSize));

  const value = params.toString();
  return value ? `?${value}` : "";
}

export const api = {
  status: () => fetchJson<StatusResponse>("/api/status"),
  runs: (query?: PageQuery) =>
    fetchJson<PagedResponse<SftpSyncRun>>(
      `/api/sftp-sync-runs${toQueryString(query)}`,
    ),
  run: (id: string) => fetchJson<SftpSyncRun>(`/api/sftp-sync-runs/${id}`),
  queueItems: (runId: string, query?: PageQuery) =>
    fetchJson<PagedResponse<DownloadQueueItem>>(
      `/api/sftp-sync-runs/${runId}/queue-items${toQueryString(query)}`,
    ),
  jobs: () => fetchJson<SftpSyncJob[]>("/api/sftp-sync-jobs"),
  createJob: (request: UpsertSftpSyncJob) =>
    fetchJson<SftpSyncJob>("/api/sftp-sync-jobs", {
      method: "POST",
      body: JSON.stringify(request),
    }),
  updateJob: (id: string, request: UpsertSftpSyncJob) =>
    fetchJson<SftpSyncJob>(`/api/sftp-sync-jobs/${id}`, {
      method: "PUT",
      body: JSON.stringify(request),
    }),
  profiles: () =>
    fetchJson<SftpConnectionProfile[]>("/api/sftp-connection-profiles"),
  createProfile: (request: UpsertSftpConnectionProfile) =>
    fetchJson<SftpConnectionProfile>("/api/sftp-connection-profiles", {
      method: "POST",
      body: JSON.stringify(request),
    }),
  updateProfile: (id: string, request: UpsertSftpConnectionProfile) =>
    fetchJson<SftpConnectionProfile>(`/api/sftp-connection-profiles/${id}`, {
      method: "PUT",
      body: JSON.stringify(request),
    }),
  runJob: (id: string) =>
    fetchJson<SftpSyncRun>(`/api/sftp-sync-jobs/${id}/run`, {
      method: "POST",
    }),
};
