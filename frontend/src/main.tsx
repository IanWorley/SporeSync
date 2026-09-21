import { useEffect, useState } from 'react';
import { createRoot } from 'react-dom/client';
import './styles.css';
import { SettingsPanel } from './SettingsPanel';

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

  return (
    <main className="min-h-screen bg-slate-950 px-6 py-16 text-slate-100">
      <section className="mx-auto max-w-3xl rounded-2xl border border-slate-800 bg-slate-900 p-8">
        <h1 className="text-3xl font-semibold tracking-tight">SporeSync</h1>
        <p className="mt-4 text-slate-300" role="status">{status}</p>
      </section>
      <div className="mx-auto mt-6 max-w-3xl"><SettingsPanel /></div>
    </main>
  );
}

const root = document.getElementById('root');
if (!root) throw new Error('Missing root element');
createRoot(root).render(<App />);
