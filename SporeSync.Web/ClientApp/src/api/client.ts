import type {
  AuthSession,
  DeleteQueueItemFileResponse,
  DownloadQueueItem,
  HostKeyScanResult,
  PagedResponse,
  RetryFailedItemsResponse,
  SftpConnectionProfile,
  SftpConnectionTestResponse,
  SporeSyncJob,
  SporeSyncRun,
  StatusResponse,
  TestSftpConnectionRequest,
  UpsertSftpConnectionProfile,
  UpsertSporeSyncJob,
} from "./types";

export interface PageQuery {
  status?: string[];
  search?: string;
  sortBy?: string;
  sortDirection?: "asc" | "desc";
  pageNumber?: number;
  pageSize?: number;
}

export class ApiError extends Error {
  readonly status: number;

  constructor(message: string, status: number) {
    super(message);
    this.status = status;
  }
}

/**
 * A 401 outside the auth endpoints means the session expired or was never
 * established, so the SPA should navigate to the login page. Auth endpoints
 * are excluded because a failed login already surfaces its own error.
 */
export function shouldRedirectToLogin(status: number, path: string) {
  return status === 401 && !path.startsWith("/api/auth/");
}

export interface ProblemDetails {
  type?: string;
  title?: string;
  status?: number;
  detail?: string;
  errors?: Record<string, string[]>;
}

export function problemDetailsMessage(body: string): string | undefined {
  try {
    const problem = JSON.parse(body) as ProblemDetails;
    const fieldErrors = Object.values(problem.errors ?? {}).flat();
    if (fieldErrors.length > 0) {
      return fieldErrors.join(" ");
    }
    return problem.detail ?? problem.title;
  } catch {
    return undefined;
  }
}

async function toRequestError(response: Response): Promise<ApiError> {
  const body = await response.text();
  const message = body
    ? (problemDetailsMessage(body) ?? body)
    : `Request failed with ${response.status}`;
  return new ApiError(message, response.status);
}

async function fetchResponse(
  path: string,
  init?: RequestInit,
): Promise<Response> {
  const response = await fetch(path, {
    ...init,
    headers: {
      Accept: "application/json",
      "Content-Type": "application/json",
      ...init?.headers,
    },
  });

  if (!response.ok) {
    if (
      shouldRedirectToLogin(response.status, path) &&
      typeof window !== "undefined"
    ) {
      window.location.assign("/login");
    }

    throw await toRequestError(response);
  }

  return response;
}

export async function fetchJson<T>(
  path: string,
  init?: RequestInit,
): Promise<T> {
  const response = await fetchResponse(path, init);
  return response.json() as Promise<T>;
}

export async function fetchNoContent(
  path: string,
  init?: RequestInit,
): Promise<void> {
  await fetchResponse(path, init);
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
  session: () => fetchJson<AuthSession>("/api/auth/session"),
  login: (username: string, password: string) =>
    fetchJson<AuthSession>("/api/auth/login", {
      method: "POST",
      body: JSON.stringify({ username, password }),
    }),
  logout: () => fetchJson<AuthSession>("/api/auth/logout", { method: "POST" }),
  status: () => fetchJson<StatusResponse>("/api/status"),
  runs: (query?: PageQuery) =>
    fetchJson<PagedResponse<SporeSyncRun>>(
      `/api/sftp-sync-runs${toQueryString(query)}`,
    ),
  run: (id: string) => fetchJson<SporeSyncRun>(`/api/sftp-sync-runs/${id}`),
  queueItems: (runId: string, query?: PageQuery) =>
    fetchJson<PagedResponse<DownloadQueueItem>>(
      `/api/sftp-sync-runs/${runId}/queue-items${toQueryString(query)}`,
    ),
  deleteQueueItemFile: (
    runId: string,
    queueItemId: string,
    target: "local" | "remote",
  ) =>
    fetchJson<DeleteQueueItemFileResponse>(
      `/api/sftp-sync-runs/${runId}/queue-items/${queueItemId}/${target}`,
      { method: "DELETE" },
    ),
  retryQueueItem: (runId: string, queueItemId: string) =>
    fetchJson<DownloadQueueItem>(
      `/api/sftp-sync-runs/${runId}/queue-items/${queueItemId}/retry`,
      { method: "POST" },
    ),
  jobs: () => fetchJson<SporeSyncJob[]>("/api/sftp-sync-jobs"),
  createJob: (request: UpsertSporeSyncJob) =>
    fetchJson<SporeSyncJob>("/api/sftp-sync-jobs", {
      method: "POST",
      body: JSON.stringify(request),
    }),
  updateJob: (id: string, request: UpsertSporeSyncJob) =>
    fetchJson<SporeSyncJob>(`/api/sftp-sync-jobs/${id}`, {
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
  scanHostKey: (host: string, port: number) =>
    fetchJson<HostKeyScanResult>(
      "/api/sftp-connection-profiles/host-key-scan",
      {
        method: "POST",
        body: JSON.stringify({ host, port }),
      },
    ),
  runJob: (id: string) =>
    fetchJson<SporeSyncRun>(`/api/sftp-sync-jobs/${id}/run`, {
      method: "POST",
    }),
  deleteJob: (id: string) =>
    fetchNoContent(`/api/sftp-sync-jobs/${id}`, { method: "DELETE" }),
  deleteProfile: (id: string) =>
    fetchNoContent(`/api/sftp-connection-profiles/${id}`, {
      method: "DELETE",
    }),
  testProfile: (request: TestSftpConnectionRequest) =>
    fetchJson<SftpConnectionTestResponse>(
      "/api/sftp-connection-profiles/test",
      { method: "POST", body: JSON.stringify(request) },
    ),
  cancelRun: (id: string) =>
    fetchJson<SporeSyncRun>(`/api/sftp-sync-runs/${id}/cancel`, {
      method: "POST",
    }),
  retryRunFailed: (id: string) =>
    fetchJson<RetryFailedItemsResponse>(
      `/api/sftp-sync-runs/${id}/retry-failed`,
      { method: "POST" },
    ),
  getDbLogLevel: () =>
    fetchJson<SystemPropertyResponse>("/api/system-properties/db_log_level"),
  setDbLogLevel: (value: string) =>
    fetchJson<SystemPropertyResponse>("/api/system-properties/db_log_level", {
      method: "PUT",
      body: JSON.stringify({ propertyValue: value }),
    }),
  getDbLogs: (minLevel?: string, limit = 100) =>
    fetchJson<{ items: DbCallLogEntry[]; currentLevel: string }>(
      `/api/system/db-logs?minLevel=${minLevel ?? ""}&limit=${limit}`,
    ),
};

export interface SystemPropertyResponse {
  id: string;
  propertyName: string;
  propertyValue: string;
}

export interface DbCallLogEntry {
  timestamp: string;
  level: string;
  operation: string;
  durationMs: number;
  paramNames: string;
  exceptionMessage: string | null;
  sqlText: string | null;
}
