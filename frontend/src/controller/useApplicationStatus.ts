import { useEffect, useState } from 'react';
import { STATUS_ENDPOINT } from '../config/api';
import { parseApplicationStatus } from '../model/ApplicationStatus';

export function useApplicationStatus(): string {
  const [status, setStatus] = useState('Connecting to backend…');

  useEffect(() => {
    const controller = new AbortController();
    async function loadStatus() {
      try {
        const response = await fetch(STATUS_ENDPOINT, { signal: controller.signal });
        if (!response.ok) throw new Error('Status request failed');
        const body = parseApplicationStatus(await response.json());
        setStatus(`Connected to ${body.application}`);
      } catch {
        if (!controller.signal.aborted) setStatus('Backend unavailable');
      }
    }
    void loadStatus();
    return () => controller.abort();
  }, []);

  return status;
}
