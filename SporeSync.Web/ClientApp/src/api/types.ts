export interface PagedResponse<T> {
  items: T[];
  pageNumber: number;
  pageSize: number;
  totalCount: number;
}

export interface SporeSyncRun {
  id: string;
  jobId: string;
  jobName: string;
  status: string;
  startedAt: string;
  completedAt: string | null;
  totalFileCount: number;
  completedFileCount: number;
  skippedFileCount: number;
  failedFileCount: number;
  totalBytes: number;
  downloadedBytes: number;
  currentBytesPerSecond: number | null;
  errorMessage: string | null;
}

export interface DownloadQueueItem {
  id: string;
  jobId: string;
  syncRunId: string | null;
  remotePath: string | null;
  destinationPath: string | null;
  fileSizeBytes: number;
  remoteModifiedAt: string | null;
  status: string;
  bytesDownloaded: number;
  currentBytesPerSecond: number | null;
  retryCount: number;
  handledReason: string | null;
  errorMessage: string | null;
  queuedAt: string;
  startedAt: string | null;
  completedAt: string | null;
  updatedAt: string;
  // Phase 3 additions (plan:339). Defaults (false/null/0) on wire for all current flat rows.
  isGroup: boolean;
  groupRemotePath: string | null;
  childCount: number;
}

export interface DeleteQueueItemFileResponse {
  queueItemId: string;
  target: "local" | "remote";
  path: string;
  existed: boolean;
}

export interface SporeSyncJob {
  id: string;
  connectionProfileId: string;
  name: string;
  sourcePath: string;
  destinationPath: string;
  pollingIntervalSeconds: number;
  isEnabled: boolean;
  lastPolledAt: string | null;
}

export interface UpsertSporeSyncJob {
  connectionProfileId: string;
  name: string;
  sourcePath: string;
  destinationPath: string;
  pollingIntervalSeconds: number;
  isEnabled: boolean;
}

export interface SftpConnectionProfile {
  id: string;
  name: string;
  host: string;
  port: number;
  username: string;
  authenticationMethod: "password" | "privateKey";
  hasPassword: boolean;
  hasPrivateKey: boolean;
  hasPrivateKeyPassphrase: boolean;
  trustedHostKeyFingerprintsSha256: string[];
  isDefault: boolean;
}

export interface UpsertSftpConnectionProfile {
  name: string;
  host: string;
  port: number;
  username: string;
  authenticationMethod: "password" | "privateKey";
  password?: string | null;
  privateKey?: string | null;
  privateKeyPassphrase?: string | null;
  removePrivateKeyPassphrase: boolean;
  // Null preserves stored fingerprints; an empty array makes connections fail closed.
  trustedHostKeyFingerprintsSha256?: string[] | null;
  isDefault: boolean;
}

export interface HostKeyScanResult {
  hostKeyAlgorithm: string;
  keyLength: number;
  fingerprintSha256: string;
}

export interface AuthSession {
  authRequired: boolean;
  authenticated: boolean;
  username: string | null;
}

export interface SftpConnectionTestResponse {
  success: boolean;
  message: string | null;
  durationMs: number;
}

export interface RetryFailedItemsResponse {
  retriedCount: number;
  run: SporeSyncRun;
}

export interface StatusResponse {
  status: string;
  environment: string;
  currentTime: string;
  databaseAvailable: boolean;
  encryptionKeyInitialized: boolean;
  encryptionKeyVersion: string;
}
