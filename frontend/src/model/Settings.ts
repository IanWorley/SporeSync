export interface Settings {
  host: string;
  port: number;
  timeoutMillis: number;
  username: string;
  source: string;
  destination: string;
  scanSeconds: number;
  automatic: boolean;
  temporaryFiles: boolean;
}

