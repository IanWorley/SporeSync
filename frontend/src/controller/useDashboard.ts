import { useEffect, useRef, useState } from 'react';
import type { DiscoverySnapshot, DownloadJob } from '../model/Inventory';
import { POLL_MILLIS } from '../config/api';
import { errorMessage, jsonBody, request } from './api';

export function useDashboard() {
  const [tab, setTab] = useState<'files' | 'downloads' | 'settings'>('files');
  const [discovery, setDiscovery] = useState<DiscoverySnapshot | null>(null);
  const [jobs, setJobs] = useState<DownloadJob[]>([]);
  const [error, setError] = useState('');
  const [connected, setConnected] = useState(false);
  const [busy, setBusy] = useState('');
  const [notice, setNotice] = useState('');
  const [query, setQuery] = useState('');
  const latestRefresh = useRef(0);

  async function refresh(signal?: AbortSignal) {
    const generation = ++latestRefresh.current;
    try {
      const [inventory, downloads] = await Promise.all([
        request<DiscoverySnapshot>('/inventory', { signal }),
        request<DownloadJob[]>('/downloads', { signal }),
      ]);
      if (generation !== latestRefresh.current || signal?.aborted) return;
      setDiscovery(inventory);
      setJobs(downloads);
      setConnected(true);
    } catch (reason) {
      if (generation !== latestRefresh.current || signal?.aborted) return;
      setConnected(false);
      throw reason;
    }
  }

  useEffect(() => {
    const controller = new AbortController();
    let timer: ReturnType<typeof setTimeout>;
    async function poll() {
      await refresh(controller.signal).catch(() => undefined);
      if (!controller.signal.aborted) timer = setTimeout(() => void poll(), POLL_MILLIS);
    }
    void poll();
    return () => {
      controller.abort();
      clearTimeout(timer);
    };
  }, []);

  async function act(key: string, path: string, body?: unknown) {
    setBusy(key);
    setError('');
    setNotice('');
    try {
      await request(path, { method: 'POST', ...(body === undefined ? {} : jsonBody(body)) });
      await refresh();
      setNotice(key === 'scan' ? 'Scan complete.' : 'Download queue updated.');
    } catch (reason) {
      setError(errorMessage(reason));
    } finally {
      setBusy('');
    }
  }

  const entries = discovery?.inventory?.entries ?? [];
  const files = entries.filter((entry) => entry.type === 'file');
  const filtered = entries.filter((entry) => entry.path.toLocaleLowerCase().includes(query.toLocaleLowerCase()));
  const running = jobs.filter((job) => job.state === 'RUNNING');
  const queued = jobs.filter((job) => job.state === 'QUEUED');
  const completed = jobs.filter((job) => job.state === 'COMPLETE');

  return { tab, setTab, discovery, jobs, error, connected, busy, notice, query, setQuery, act, entries, files, filtered, running, queued, completed };
}
