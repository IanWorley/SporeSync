import { useEffect, useRef, useState } from 'react';
import type { DownloadJob, JobAction, JobState } from '../model/Inventory';
import { useDashboard } from '../controller/useDashboard';
import { SettingsPanel } from './SettingsPanel';

const BYTE_FRACTION_DIGITS = 1;
const BYTES_PER_UNIT = 1024;
const PERCENT = 100;
const BYTE_UNITS = ['B', 'KiB', 'MiB', 'GiB', 'TiB'];
const BUTTON_BASE = 'rounded-lg border px-3 py-2 text-sm font-medium disabled:cursor-wait disabled:opacity-40';
const BUTTON = `${BUTTON_BASE} border-slate-700 text-slate-200 hover:border-emerald-400 hover:text-emerald-300`;
const DELETE_BUTTON = `${BUTTON_BASE} border-rose-800 text-rose-300 hover:border-rose-400 hover:text-rose-200`;
const STATE_COLORS: Record<JobState, string> = {
  QUEUED: 'bg-slate-800 text-slate-300',
  RUNNING: 'bg-sky-950 text-sky-300',
  COMPLETE: 'bg-emerald-950 text-emerald-300',
  FAILED: 'bg-rose-950 text-rose-300',
  CANCELLED: 'bg-amber-950 text-amber-300',
  PAUSED: 'bg-amber-950 text-amber-300',
  REMOTE_DELETED: 'bg-slate-800 text-slate-400',
};
const ACTION_LABELS: Record<JobAction, string> = {
  PAUSE: 'Pausing…',
  DELETE_LOCAL: 'Deleting local copy…',
  DELETE_REMOTE: 'Deleting server copy…',
};
type Deletion = { job: DownloadJob; target: 'local' | 'remote' };

function bytes(value: number): string {
  let amount = value;
  let unit = 0;
  while (amount >= BYTES_PER_UNIT && unit < BYTE_UNITS.length - 1) {
    amount /= BYTES_PER_UNIT;
    unit++;
  }
  return `${amount.toLocaleString(undefined, { maximumFractionDigits: BYTE_FRACTION_DIGITS })} ${BYTE_UNITS[unit]}`;
}

export function App() {
  const { tab, setTab, discovery, jobs, error, connected, busy, notice, query, setQuery, act, entries, files, filtered, running, queued, completed } = useDashboard();
  const [deletion, setDeletion] = useState<Deletion | null>(null);
  const confirmation = useRef<HTMLDialogElement>(null);

  useEffect(() => {
    if (deletion) confirmation.current?.showModal();
    else confirmation.current?.close();
  }, [deletion]);

  return (
    <main className="min-h-screen bg-slate-950 px-4 py-8 text-slate-100 sm:px-8 sm:py-12">
      <div className="mx-auto max-w-6xl">
        <header className="flex flex-wrap items-start justify-between gap-5">
          <div>
            <p className="text-xs font-semibold uppercase tracking-widest text-emerald-400">Seedbox → your storage</p>
            <h1 className="mt-2 text-4xl font-semibold tracking-tight">SporeSync</h1>
            <p className="mt-3 text-sm text-slate-400">Your files, arriving in the background.</p>
          </div>
          <span
            role="status"
            className={`rounded-full border px-3 py-1.5 text-xs ${connected ? 'border-emerald-900 text-emerald-300' : 'border-amber-900 text-amber-300'}`}
          >
            {connected ? 'Backend connected' : 'Waiting for backend…'}
          </span>
        </header>
        <section aria-label="Download summary" className="my-8 grid grid-cols-2 gap-3 sm:grid-cols-4">
          {[
            ['Discovered files', files.length],
            ['Downloading', running.length],
            ['In queue', queued.length],
            ['Completed', completed.length],
          ].map(([label, count]) => (
            <div key={label} className="rounded-xl border border-slate-800 bg-slate-900/60 p-5">
              <p className="text-xs text-slate-400">{label}</p>
              <p className="mt-2 text-3xl font-semibold tabular-nums">{count}</p>
            </div>
          ))}
        </section>
        <nav aria-label="Dashboard sections" className="mb-6 flex gap-2 border-b border-slate-800">
          {(['files', 'downloads', 'settings'] as const).map((item) => (
            <button
              key={item}
              aria-pressed={tab === item}
              onClick={() => setTab(item)}
              className={`border-b-2 px-4 pb-3 text-sm capitalize ${tab === item ? 'border-emerald-400 text-emerald-300' : 'border-transparent text-slate-400 hover:text-white'}`}
            >
              {item}
            </button>
          ))}
        </nav>
        {error && (
          <p role="alert" className="mb-4 rounded-lg border border-rose-900 bg-rose-950/40 p-4 text-sm text-rose-300">
            {error}
          </p>
        )}
        <p role="status" className="mb-4 text-sm text-emerald-300">
          {notice}
        </p>
        {tab === 'settings' && <SettingsPanel />}
        {tab === 'files' && (
          <section
            className="overflow-hidden rounded-2xl border border-slate-800 bg-slate-900/50"
            aria-labelledby="files-title"
          >
            <div className="flex flex-wrap items-center justify-between gap-4 p-6">
              <div>
                <h2 id="files-title" className="text-xl font-semibold">
                  Remote files
                </h2>
                <p className="mt-2 text-xs text-slate-400">
                  {discovery?.lastSuccess
                    ? `Last scan ${new Date(discovery.lastSuccess).toLocaleString()}`
                    : 'Configure your connection, then scan to discover files.'}
                </p>
              </div>
              <button
                className={BUTTON}
                disabled={!!busy || !connected}
                onClick={() => void act('scan', '/inventory/scan')}
              >
                {busy === 'scan' ? 'Scanning…' : 'Scan now'}
              </button>
            </div>
            {discovery?.error && (
              <p role="alert" className="mx-6 mb-4 text-sm text-amber-300">
                Last scan failed: {discovery.error}. Check settings and backend SSH configuration. Previously discovered
                files may be stale.
              </p>
            )}
            {entries.length > 0 && (
              <div className="px-6 pb-5">
                <label className="sr-only" htmlFor="file-filter">
                  Filter files
                </label>
                <input
                  id="file-filter"
                  value={query}
                  onChange={(event) => setQuery(event.target.value)}
                  placeholder="Filter by filename or folder…"
                  className="w-full rounded-lg border border-slate-700 bg-slate-950 px-3 py-2 text-sm outline-none focus:border-emerald-400"
                />
              </div>
            )}
            {filtered.length === 0 ? (
              <p className="border-t border-slate-800 px-6 py-14 text-center text-sm text-slate-400">
                {entries.length ? 'No files match your filter.' : 'No files discovered yet.'}
              </p>
            ) : (
              <div className="overflow-x-auto">
                <table className="w-full text-left text-sm">
                  <thead className="border-y border-slate-800 text-xs text-slate-500">
                    <tr>
                      <th className="px-6 py-3">Path</th>
                      <th className="px-4 py-3">Type</th>
                      <th className="px-4 py-3">Size</th>
                      <th className="px-6 py-3">
                        <span className="sr-only">Action</span>
                      </th>
                    </tr>
                  </thead>
                  <tbody className="divide-y divide-slate-800">
                    {filtered.map((entry) => (
                      <tr key={entry.path} className="hover:bg-slate-800/40">
                        <td className="max-w-md break-words px-6 py-4 font-medium">{entry.path}</td>
                        <td className="px-4 py-4 text-slate-400">{entry.type}</td>
                        <td className="whitespace-nowrap px-4 py-4 tabular-nums text-slate-400">
                          {entry.type === 'file' ? bytes(entry.sizeBytes) : '—'}
                        </td>
                        <td className="px-6 py-4 text-right">
                          {entry.type === 'file' && (
                            <button
                              className={BUTTON}
                              disabled={!!busy || !connected}
                              aria-label={`Download ${entry.path}`}
                              onClick={() => void act(entry.path, '/downloads', { path: entry.path })}
                            >
                              Download
                            </button>
                          )}
                        </td>
                      </tr>
                    ))}
                  </tbody>
                </table>
              </div>
            )}
          </section>
        )}
        {tab === 'downloads' && (
          <section
            aria-labelledby="downloads-title"
            className="rounded-2xl border border-slate-800 bg-slate-900/50 p-6"
          >
            <h2 id="downloads-title" className="text-xl font-semibold">
              Download queue
            </h2>
            <p className="mt-2 text-sm text-slate-400">
              One file at a time. Transfers continue when you close this page.
            </p>
            {jobs.length === 0 && (
              <p className="py-14 text-center text-sm text-slate-400">
                Your queue is empty. Download a file or enable automatic downloads.
              </p>
            )}
            <div className="mt-6 space-y-4">
              {jobs.map((job) => {
                const progress =
                  job.state === 'COMPLETE'
                    ? PERCENT
                    : job.spec.entry.sizeBytes > 0
                      ? Math.min(PERCENT, (job.bytesDone / job.spec.entry.sizeBytes) * PERCENT)
                      : 0;
                return (
                  <article key={job.id} className="rounded-xl border border-slate-800 bg-slate-950/50 p-5">
                    <div className="flex flex-wrap items-start justify-between gap-3">
                      <div className="min-w-0">
                        <h3 className="break-words text-sm font-medium">{job.spec.entry.path}</h3>
                        <p className="mt-1 break-all text-xs text-slate-500">{job.spec.settings.destination}</p>
                      </div>
                      <span className={`rounded-full px-2.5 py-1 text-xs ${STATE_COLORS[job.state]}`}>
                        {job.action ? ACTION_LABELS[job.action] : job.cancelRequested && job.state === 'RUNNING' ? 'Cancelling…' : job.state.replaceAll('_', ' ').toLowerCase()}
                      </span>
                    </div>
                    <progress
                      aria-label={`Progress for ${job.spec.entry.path}`}
                      className="mt-4 h-1.5 w-full accent-emerald-400"
                      value={progress}
                      max={PERCENT}
                    />
                    <div className="mt-3 flex flex-wrap items-center justify-between gap-3">
                      <p className="text-xs tabular-nums text-slate-400">
                        {bytes(job.bytesDone)} / {bytes(job.spec.entry.sizeBytes)} · {Math.floor(progress)}% · Attempts:{' '}
                        {job.attempts}
                      </p>
                      <div className="flex flex-wrap gap-2">
                        {(job.state === 'QUEUED' || job.state === 'RUNNING') && (
                          <button
                            className={BUTTON}
                            disabled={!!busy || !!job.action || job.cancelRequested || !connected}
                            aria-label={`Pause ${job.spec.entry.path}`}
                            onClick={() => void act(job.id, `/downloads/${job.id}/pause`)}
                          >
                            Pause
                          </button>
                        )}
                        {job.state === 'PAUSED' && (
                          <button
                            className={BUTTON}
                            disabled={!!busy || !!job.action || !connected}
                            aria-label={`Resume ${job.spec.entry.path}`}
                            onClick={() => void act(job.id, `/downloads/${job.id}/resume`)}
                          >
                            Resume
                          </button>
                        )}
                        {(job.state === 'QUEUED' || job.state === 'RUNNING') && (
                          <button
                            className={BUTTON}
                            disabled={!!busy || !!job.action || job.cancelRequested || !connected}
                            onClick={() => void act(job.id, `/downloads/${job.id}/cancel`)}
                          >
                            Cancel
                          </button>
                        )}
                        {(job.state === 'FAILED' || job.state === 'CANCELLED') && (
                          <button
                            className={BUTTON}
                            disabled={!!busy || !!job.action || !connected}
                            onClick={() => void act(job.id, `/downloads/${job.id}/retry`)}
                          >
                            Retry
                          </button>
                        )}
                        <button
                          className={BUTTON}
                          disabled={!!busy || !!job.action || !connected}
                          aria-label={`Delete local copy of ${job.spec.entry.path}`}
                          onClick={() => setDeletion({ job, target: 'local' })}
                        >
                          Delete local
                        </button>
                        {job.state !== 'REMOTE_DELETED' && (
                          <button
                            className={BUTTON}
                            disabled={!!busy || !!job.action || !connected}
                            aria-label={`Delete server copy of ${job.spec.entry.path}`}
                            onClick={() => setDeletion({ job, target: 'remote' })}
                          >
                            Delete from server
                          </button>
                        )}
                      </div>
                    </div>
                    {job.error && <p className="mt-3 text-xs text-amber-300">{job.error}</p>}
                  </article>
                );
              })}
            </div>
          </section>
        )}
        <footer className="mt-8 text-xs leading-6 text-slate-500">
          One-way copies. Server files stay in place unless you delete them explicitly. Progress refreshes automatically.
        </footer>
        <dialog
          ref={confirmation}
          aria-labelledby="delete-title"
          aria-describedby="delete-description"
          onCancel={() => setDeletion(null)}
          className="m-auto w-full max-w-lg rounded-2xl border border-slate-700 bg-slate-900 p-6 text-slate-100 shadow-xl backdrop:bg-black/70"
        >
          {deletion && (
            <>
              <h2 id="delete-title" className="text-xl font-semibold">
                Delete {deletion.target === 'local' ? 'local' : 'server'} copy?
              </h2>
              <p className="mt-4 break-all text-sm font-medium">{deletion.job.spec.entry.path}</p>
              <p className="mt-2 break-all text-xs text-slate-400">
                {deletion.target === 'local' ? deletion.job.spec.settings.destination : `${deletion.job.spec.settings.host}:${deletion.job.spec.settings.source}`}
              </p>
              <p id="delete-description" className="mt-4 text-sm text-slate-300">
                {deletion.target === 'local'
                  ? deletion.job.state === 'REMOTE_DELETED'
                    ? 'This permanently removes the local copy and any partial download. The server copy was deleted, so it cannot be downloaded again.'
                    : 'This permanently removes the local copy and any partial download, then queues the file to download again. The server copy stays in place.'
                  : 'This permanently deletes the file from the server and stops its downloads. Your local copy stays in place.'}
              </p>
              <div className="mt-6 flex justify-end gap-3">
                <button autoFocus className={BUTTON} onClick={() => setDeletion(null)}>Keep file</button>
                <button
                  className={DELETE_BUTTON}
                  disabled={!!busy || !connected}
                  onClick={() => {
                    void act(deletion.job.id, `/downloads/${deletion.job.id}/delete-${deletion.target}`);
                    setDeletion(null);
                  }}
                >
                  {deletion.target === 'local' ? 'Delete local copy' : 'Delete server copy'}
                </button>
              </div>
            </>
          )}
        </dialog>
      </div>
    </main>
  );
}
