import type { Settings } from './Settings';

export interface InventoryEntry {
  path: string;
  type: 'file' | 'directory' | 'symlink' | 'other';
  sizeBytes: number;
  modifiedTimeNs: number;
}

export interface Inventory {
  schemaVersion: number;
  entries: InventoryEntry[];
}

export interface DiscoverySnapshot {
  inventory: Inventory | null;
  lastAttempt: string | null;
  lastSuccess: string | null;
  error: string | null;
}

export type JobState = 'QUEUED' | 'RUNNING' | 'COMPLETE' | 'FAILED' | 'CANCELLED';
export interface DownloadSpec {
  settings: Settings;
  entry: InventoryEntry;
}

export interface DownloadJob {
  id: string;
  spec: DownloadSpec;
  state: JobState;
  bytesDone: number;
  attempts: number;
  error: string | null;
  cancelRequested: boolean;
}
