import { useEffect, useState } from 'react';
import { createRoot } from 'react-dom/client';

function App() {
  const [status, setStatus] = useState('Connecting to backend…');

  useEffect(() => {
    const controller = new AbortController();
    async function loadStatus() {
      try {
        const response = await fetch('/api/status', { signal: controller.signal });
        if (!response.ok) throw new Error('Status request failed');
        const body: unknown = await response.json();
        if (typeof body !== 'object' || body === null ||
            !('application' in body) || typeof body.application !== 'string') {
          throw new Error('Invalid status response');
        }
        setStatus(`Connected to ${body.application}`);
      } catch {
        if (!controller.signal.aborted) setStatus('Backend unavailable');
      }
    }
    void loadStatus();
    return () => controller.abort();
  }, []);

  return <main><h1>SporeSync</h1><p role="status">{status}</p></main>;
}

const root = document.getElementById('root');
if (!root) throw new Error('Missing root element');
createRoot(root).render(<App />);
