export interface Settings {
  host: string;
  port: number;
  username: string;
  source: string;
  destination: string;
  scanSeconds: number;
  automatic: boolean;
  temporaryFiles: boolean;
}

export async function request<T>(path: string, options?: RequestInit): Promise<T> {
  const response = await fetch(`/api${path}`, options);
  if (!response.ok) {
    const body: unknown = await response.json().catch(() => null);
    const code = typeof body === 'object' && body !== null && 'error' in body && typeof body.error === 'string'
      ? body.error : `Request failed (${response.status})`;
    throw new Error(code);
  }
  return response.json() as Promise<T>;
}

export function jsonBody(value: unknown): RequestInit {
  return { headers: { 'Content-Type': 'application/json' }, body: JSON.stringify(value) };
}

export function errorMessage(error: unknown): string {
  return error instanceof Error ? error.message : 'Connection unavailable';
}
